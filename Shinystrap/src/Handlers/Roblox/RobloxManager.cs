using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Shinystrap.Handlers.Web;
using Shinystrap.Pages;

namespace Shinystrap.Handlers.Roblox;

public class RobloxManager
{
    public static ObservableCollection<RobloxInstances.RobloxInstance> ActiveInstances { get; } = [];
    public static ObservableCollection<GameHistory.GameHistoryItem> GameHistory { get; } = [];
    private static readonly DispatcherTimer ScanTimer;
    private static readonly HttpHandler HttpHandler = new();

    static RobloxManager()
    {
        ScanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        ScanTimer.Tick += (_, _) => ScanForInstances();
        ScanTimer.Start();
    }
    
    private static async void ScanForInstances()
    {
        var currentProcesses = Process.GetProcessesByName("RobloxPlayerBeta");

        var pidsRunning = currentProcesses.Select(p => p.Id).ToList();
        for (int i = ActiveInstances.Count - 1; i >= 0; i--)
        {
            if (!pidsRunning.Contains(ActiveInstances[i].ProcessId))
                ActiveInstances.RemoveAt(i);
        }

        foreach (var proc in currentProcesses)
        {
            if (ActiveInstances.Any(x => x.ProcessId == proc.Id)) continue;

            var rbxId = GetRobloxIdFromLatestLog();
            var placeId = GetLatestPlaceIdFromLog();
            
            if (string.IsNullOrEmpty(rbxId) || string.IsNullOrEmpty(placeId)) continue;
            
            try
            {
                var gameThumbReq = await HttpHandler.SendAsync(
                    $"https://thumbnails.roblox.com/v1/places/gameicons?placeIds={placeId}&size=512x512&format=Png&isCircular=false",
                    HttpMethod.Get);
                var gameThumbJson = JsonDocument.Parse(await gameThumbReq.Content.ReadAsStringAsync());
                var placeImageUrl = gameThumbJson.RootElement.GetProperty("data")[0].GetProperty("imageUrl").GetString();

                var gameIcon = new System.Windows.Media.Imaging.BitmapImage();
                gameIcon.BeginInit();
                gameIcon.UriSource = new Uri(placeImageUrl ?? "");
                gameIcon.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                gameIcon.EndInit();
                gameIcon.Freeze();
                
                var uniReq = await HttpHandler.SendAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe", HttpMethod.Get);
                var uniJson = JsonDocument.Parse(await uniReq.Content.ReadAsStringAsync());
                var universeId = uniJson.RootElement.GetProperty("universeId").ToString();
                
                var nameReq = await HttpHandler.SendAsync($"https://games.roblox.com/v1/games?universeIds={universeId}", HttpMethod.Get);
                var nameJson = JsonDocument.Parse(await nameReq.Content.ReadAsStringAsync());
                var gameName = nameJson.RootElement.GetProperty("data")[0].GetProperty("name").GetString() ?? "Unknown Game";
                
                var thumbTask = HttpHandler.SendAsync(
                    $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={rbxId}&size=150x150&format=Png&isCircular=true",
                    HttpMethod.Get);
                var userTask = HttpHandler.SendAsync($"https://users.roblox.com/v1/users/{rbxId}", HttpMethod.Get);

                await Task.WhenAll(thumbTask, userTask);

                var userThumbJson = JsonDocument.Parse(await thumbTask.Result.Content.ReadAsStringAsync());
                var userImageUrl = userThumbJson.RootElement.GetProperty("data")[0].GetProperty("imageUrl").GetString();

                var userJson = JsonDocument.Parse(await userTask.Result.Content.ReadAsStringAsync());
                var robloxName = userJson.RootElement.GetProperty("name").GetString() ?? "Untitled";

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (GameHistory.Count == 0 || GameHistory[0].Description != $"Place ID: {placeId}")
                    {
                        GameHistory.Insert(0, new GameHistory.GameHistoryItem
                        {
                            Title = gameName,
                            Description = $"Place ID: {placeId}, Account: {robloxName}", 
                            ImageSource = gameIcon,
                            PlaceId = placeId
                        });
                    }
                });
                
                var userBitmap = new System.Windows.Media.Imaging.BitmapImage();
                userBitmap.BeginInit();
                userBitmap.UriSource = new Uri(userImageUrl ?? "https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=1&size=150x150&format=Png&isCircular=true");
                userBitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                userBitmap.EndInit();
                userBitmap.Freeze();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ActiveInstances.Add(new RobloxInstances.RobloxInstance
                    {
                        Title = robloxName,
                        Description = $"PID: {proc.Id} | Started: {proc.StartTime.ToShortTimeString()}",
                        ImageSource = userBitmap,
                        ProcessId = proc.Id
                    });
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load instance: {ex.Message}");
            }
        }
    }

    private static string? GetRobloxIdFromLatestLog()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox", "logs");

        var latestLog = Directory.EnumerateFiles(logDir, "*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault();

        if (latestLog is null)
            return null;

        try
        {
            using var fs = new FileStream(
                latestLog.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            using var reader = new StreamReader(fs);

            while (reader.ReadLine() is { } line)
            {
                var match = Regex.Match(line, @"userid:(\d+)");
                if (match.Success)
                    return match.Groups[1].Value;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return null;
    }
    
    public static string? GetLatestPlaceIdFromLog()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox", "logs");

        var latestLog = Directory.EnumerateFiles(logDir, "*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault();

        if (latestLog is null) return null;

        try
        {
            using var fs = new FileStream(latestLog.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            
            string? lastFoundPlaceId = null;

            while (reader.ReadLine() is { } line)
            {
                var match = Regex.Match(line, @"Joining game '.+?' place (\d+)");
                if (match.Success)
                {
                    lastFoundPlaceId = match.Groups[1].Value;
                }
            }
            return lastFoundPlaceId;
        }
        catch { return null; }
    }
}