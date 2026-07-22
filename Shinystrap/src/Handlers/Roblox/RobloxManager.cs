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

public sealed class RobloxManager
{
    private static readonly string RobloxLogsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox",
        "logs");

    private static readonly Regex JoinInfoRegex =
        new(@"universeid:(\d+).*?userid:(\d+)", RegexOptions.Compiled);

    private static readonly Regex PlaceIdRegex =
        new(@"Joining game '.+?' place (\d+)", RegexOptions.Compiled);
    
    public static ObservableCollection<RobloxInstances.RobloxInstance> ActiveInstances { get; } = [];
    public static ObservableCollection<GameHistory.GameHistoryItem> GameHistory { get; } = [];
    
    public static string? RobloxBiscuit = "";
    
    private static readonly HashSet<int> KnownProcesses = [];

    private static readonly DispatcherTimer ScanTimer;
    
    private static readonly HttpHandler HttpHandler = new();
    
    static RobloxManager()
    {
        ScanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        ScanTimer.Tick += async (_, _) => await ScanInstancesAsync();
        ScanTimer.Start();
    }
    

    private static async Task ScanInstancesAsync()
    {
        var currentProcesses = Process.GetProcessesByName("RobloxPlayerBeta");
        var runningProcessIds = currentProcesses.Select(p => p.Id).ToHashSet();

        KnownProcesses.RemoveWhere(pid => !runningProcessIds.Contains(pid));

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            for (int i = ActiveInstances.Count - 1; i >= 0; i--)
            {
                if (!runningProcessIds.Contains(ActiveInstances[i].ProcessId))
                    ActiveInstances.RemoveAt(i);
            }
        });

        foreach (var process in currentProcesses)
        {
            if (!KnownProcesses.Add(process.Id))
                continue;
            
            var started = process.StartTime.ToUniversalTime();

            var log = await WaitForRobloxLog(started);
            
            if (log == null)
            {
                continue;
            }
            
            var details = GetRobloxDetails(log);

            var userTask = await HttpHandler.SendAsync($"https://users.roblox.com/v1/users/{details.UserId}", HttpMethod.Get);
            var response = await userTask.Content.ReadAsStringAsync();
            using var userDoc = JsonDocument.Parse(response);
            var robloxName = userDoc.RootElement.GetProperty("name").GetString() ?? "Untitled";
            
            var thumbTask = await HttpHandler.SendAsync(
                $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={details.UserId}&size=150x150&format=Png&isCircular=true",
                HttpMethod.Get);

            var thumbTaskResponse = await thumbTask.Content.ReadAsStringAsync();
            
            using var userThumbDoc = JsonDocument.Parse(thumbTaskResponse);
            var userImageUrl = userThumbDoc.RootElement.GetProperty("data")[0].GetProperty("imageUrl").GetString();
            
            var nameReq = await HttpHandler.SendAsync(
                $"https://games.roblox.com/v1/games?universeIds={details.UniverseId}",
                HttpMethod.Get);

            using var nameDoc = JsonDocument.Parse(await nameReq.Content.ReadAsStringAsync());
            var gameName = nameDoc.RootElement.GetProperty("data")[0].GetProperty("name").GetString() ?? "Unknown Game";
            
            var gameThumbReq = await HttpHandler.SendAsync(
                $"https://thumbnails.roblox.com/v1/places/gameicons?placeIds={details.PlaceId}&size=512x512&format=Png&isCircular=false",
                HttpMethod.Get);

            using var gameThumbDoc = JsonDocument.Parse(await gameThumbReq.Content.ReadAsStringAsync());
            var placeImageUrl = gameThumbDoc.RootElement
                .GetProperty("data")[0]
                .GetProperty("imageUrl")
                .GetString();

            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (GameHistory.All(x => x.PlaceId != details.PlaceId || !x.Description.Contains(robloxName)))
                {
                    GameHistory.Insert(0, new GameHistory.GameHistoryItem
                    {
                        Title = gameName,
                        Description = $"Place ID: {details.PlaceId}, Account: {robloxName}",
                        ImageSource = placeImageUrl,
                        PlaceId = details.PlaceId
                    });
                }

                
                ActiveInstances.Add(new RobloxInstances.RobloxInstance
                {
                    Title = robloxName,
                    Description = $"PID: {process.Id} | Started: {process.StartTime.ToShortTimeString()}",
                    ImageSource = userImageUrl,
                    ProcessId = process.Id
                });
            });

        }
    }
    
    private static async Task<FileInfo?> WaitForRobloxLog(DateTime started)
    {
        for (int i = 0; i < 30; i++)
        {
            var log = GetLatestLogAfter(started);

            if (log != null && GetRobloxDetails(log).UserId != null)
                return log;

            await Task.Delay(500);
        }

        return null;
    }
    
    private static FileInfo? GetLatestLogAfter(DateTime startTime)
    {
        return Directory.EnumerateFiles(RobloxLogsPath, "*.log")
            .Select(x => new FileInfo(x))
            .Where(x => x.CreationTimeUtc >= startTime)
            .OrderByDescending(x => x.CreationTimeUtc)
            .FirstOrDefault();
    }
    
    private static (string? UserId, string? UniverseId, string? PlaceId) GetRobloxDetails(FileInfo log)
    {
        string? userId = null;
        string? universeId = null;
        string? placeId = null;

        try
        {
            using var fs = new FileStream(
                log.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            using var reader = new StreamReader(fs);

            while (reader.ReadLine() is { } line)
            {
                if (line.Contains("game_join_loadtime"))
                {
                    var match = JoinInfoRegex.Match(line);

                    if (match.Success)
                    {
                        universeId = match.Groups[1].Value;
                        userId = match.Groups[2].Value;
                    }
                }

                var placeMatch = PlaceIdRegex.Match(line);

                if (placeMatch.Success)
                {
                    placeId = placeMatch.Groups[1].Value;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed reading Roblox log: {ex.Message}");
        }

        return (userId, universeId, placeId);
    }
    
}