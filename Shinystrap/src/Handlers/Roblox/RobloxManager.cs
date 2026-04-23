using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shinystrap.Handlers.Web;
using Shinystrap.Pages;

namespace Shinystrap.Handlers.Roblox;

public sealed class RobloxManager
{
    private const string RobloxProcessName = "RobloxPlayerBeta";
    private static readonly string RobloxLogsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox",
        "logs");

    private static readonly Regex UserIdRegex = new(@"userid:(\d+)", RegexOptions.Compiled);
    private static readonly Regex PlaceIdRegex = new(@"Joining game '.+?' place (\d+)", RegexOptions.Compiled);

    public static ObservableCollection<RobloxInstances.RobloxInstance> ActiveInstances { get; } = [];
    public static ObservableCollection<GameHistory.GameHistoryItem> GameHistory { get; } = [];

    private static readonly DispatcherTimer ScanTimer;
    private static readonly HttpHandler HttpHandler = new();
    private static readonly SemaphoreSlim ScanGate = new(1, 1);

    static RobloxManager()
    {
        ScanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        ScanTimer.Tick += async (_, _) => await ScanForInstancesAsync();
        ScanTimer.Start();
    }
    
    private static async Task ScanForInstancesAsync()
    {
        if (!await ScanGate.WaitAsync(0))
        {
            return;
        }
        
        try
        {
            var currentProcesses = Process.GetProcessesByName(RobloxProcessName);
            var runningProcessIds = currentProcesses.Select(p => p.Id).ToHashSet();

            for (var i = ActiveInstances.Count - 1; i >= 0; i--)
            {
                if (!runningProcessIds.Contains(ActiveInstances[i].ProcessId))
                {
                    ActiveInstances.RemoveAt(i);
                }
            }

            foreach (var proc in currentProcesses)
            {
                if (ActiveInstances.Any(x => x.ProcessId == proc.Id))
                {
                    continue;
                }

                var rbxId = GetRobloxIdFromLatestLog();
                var placeId = GetLatestPlaceIdFromLog();

                if (string.IsNullOrWhiteSpace(rbxId) || string.IsNullOrWhiteSpace(placeId))
                {
                    continue;
                }

                try
                {
                    var gameThumbReq = await HttpHandler.SendAsync(
                        $"https://thumbnails.roblox.com/v1/places/gameicons?placeIds={placeId}&size=512x512&format=Png&isCircular=false",
                        HttpMethod.Get);

                    using var gameThumbDoc = JsonDocument.Parse(await gameThumbReq.Content.ReadAsStringAsync());
                    var placeImageUrl = gameThumbDoc.RootElement
                        .GetProperty("data")[0]
                        .GetProperty("imageUrl")
                        .GetString();

                    if (string.IsNullOrWhiteSpace(placeImageUrl))
                    {
                        continue;
                    }

                    var gameIcon = CreateFrozenBitmap(placeImageUrl);

                    var uniReq = await HttpHandler.SendAsync(
                        $"https://apis.roblox.com/universes/v1/places/{placeId}/universe",
                        HttpMethod.Get);

                    using var uniDoc = JsonDocument.Parse(await uniReq.Content.ReadAsStringAsync());
                    var universeId = uniDoc.RootElement.GetProperty("universeId").ToString();

                    var nameReq = await HttpHandler.SendAsync(
                        $"https://games.roblox.com/v1/games?universeIds={universeId}",
                        HttpMethod.Get);

                    using var nameDoc = JsonDocument.Parse(await nameReq.Content.ReadAsStringAsync());
                    var gameName = nameDoc.RootElement.GetProperty("data")[0].GetProperty("name").GetString() ?? "Unknown Game";

                    var thumbTask = HttpHandler.SendAsync(
                        $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={rbxId}&size=150x150&format=Png&isCircular=true",
                        HttpMethod.Get);

                    var userTask = HttpHandler.SendAsync(
                        $"https://users.roblox.com/v1/users/{rbxId}",
                        HttpMethod.Get);

                    await Task.WhenAll(thumbTask, userTask);

                    using var userThumbDoc = JsonDocument.Parse(await (await thumbTask).Content.ReadAsStringAsync());
                    var userImageUrl = userThumbDoc.RootElement.GetProperty("data")[0].GetProperty("imageUrl").GetString();

                    using var userDoc = JsonDocument.Parse(await (await userTask).Content.ReadAsStringAsync());
                    var robloxName = userDoc.RootElement.GetProperty("name").GetString() ?? "Untitled";

                    var userBitmap = string.IsNullOrWhiteSpace(userImageUrl)
                        ? CreateFrozenBitmap("https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=1&size=150x150&format=Png&isCircular=true")
                        : CreateFrozenBitmap(userImageUrl);

                    await Application.Current.Dispatcher.InvokeAsync(() =>
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
                    Debug.WriteLine($"Failed to load Roblox instance {proc.Id}: {ex}");
                }
            }
        }
        finally
        {
            ScanGate.Release();
        }
    }
    
    private static BitmapImage CreateFrozenBitmap(string imageUrl)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(imageUrl);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static string? GetRobloxIdFromLatestLog()
    {
        var latestLog = GetLatestLogFile();
        if (latestLog is null)
        {
            return null;
        }

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
                var match = UserIdRegex.Match(line);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Failed to read Roblox log for user id: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Access denied reading Roblox log for user id: {ex.Message}");
        }

        return null;
    }

    private static string? GetLatestPlaceIdFromLog()
    {
        var latestLog = GetLatestLogFile();
        if (latestLog is null)
        {
            return null;
        }

        try
        {
            using var fs = new FileStream(
                latestLog.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            using var reader = new StreamReader(fs);

            string? lastFoundPlaceId = null;

            while (reader.ReadLine() is { } line)
            {
                var match = PlaceIdRegex.Match(line);
                if (match.Success)
                {
                    lastFoundPlaceId = match.Groups[1].Value;
                }
            }

            return lastFoundPlaceId;
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Failed to read Roblox log for place id: {ex.Message}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Access denied reading Roblox log for place id: {ex.Message}");
            return null;
        }
    }
    
    private static FileInfo? GetLatestLogFile()
    {
        if (!Directory.Exists(RobloxLogsPath))
        {
            return null;
        }

        return Directory.EnumerateFiles(RobloxLogsPath, "*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
    }
}