using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Shinystrap.Handlers.Shinystrap;
using Shinystrap.Handlers.Web;

namespace Shinystrap.Handlers.Roblox;

public class RobloxApi
{
    private readonly HttpHandler _handler = new();
    
    public async Task<bool> CheckForUpdatesAsync()
    {
        var latestVersion = await GetRobloxVersionAsync();

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\roblox-player\shell\open\command");
        var installedVersion = key?.GetValue("version")?.ToString();

        return !string.Equals(installedVersion, latestVersion, StringComparison.Ordinal);
    }

    public async Task<string> GetRobloxVersionAsync()
    {
        var request = await _handler.SendAsync("https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer", HttpMethod.Get);
        request.EnsureSuccessStatusCode();

        var response = await request.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(response);

        if (!document.RootElement.TryGetProperty("clientVersionUpload", out var versionElement))
            throw new InvalidOperationException(
                "Roblox version response did not contain a valid clientVersionUpload value.");
        var version = versionElement.GetString();

        return !string.IsNullOrWhiteSpace(version) ? version : throw new InvalidOperationException("Roblox version response did not contain a valid clientVersionUpload value.");
    }
    
    private async Task<string?> GetCsrfToken(string? cookie)
    {
        var request = await _handler.SendAsync("https://auth.roblox.com/v1/authentication-ticket", HttpMethod.Post, new[]
        {
            new HttpHandler.RequestHeadersEx("Referer", "https://www.roblox.com/"),
            new HttpHandler.RequestHeadersEx("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:150.0) Gecko/20100101 Firefox/150.0"),
            new HttpHandler.RequestHeadersEx("Cookie", $".ROBLOSECURITY={cookie}")
        });

        request.Headers.TryGetValues("X-CSRF-TOKEN", out var headerValues);
        
        var headerValue = headerValues.FirstOrDefault();

        return headerValue;
    }
    
    private async Task<string?> ClientAssertion(string cookie)
    {
        var request = await _handler.SendAsync("https://auth.roblox.com/v1/client-assertion/", HttpMethod.Get, new[]
        {
            new HttpHandler.RequestHeadersEx("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:129.0) Gecko/20100101 Firefox/129.0"),
            new HttpHandler.RequestHeadersEx("Cookie", $".ROBLOSECURITY={cookie}")
        });

        var response = await request.Content.ReadAsStringAsync();
        
        using var document = JsonDocument.Parse(response);

        var clientAssertion = document.RootElement.GetProperty("clientAssertion").ToString();
        
        return clientAssertion;
    }
    
    public async Task<string?> GetAuthenticationTicketAsync(string? cookie)
    {
        var request = await _handler.SendAsync("https://auth.roblox.com/v1/authentication-ticket", HttpMethod.Post, $"{{ \"clientAssertion\": \"{await ClientAssertion(cookie) }\" }}",new[]
        {
            new HttpHandler.RequestHeadersEx("Referer", "https://www.roblox.com/"),
            new HttpHandler.RequestHeadersEx("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:150.0) Gecko/20100101 Firefox/150.0"),
            new HttpHandler.RequestHeadersEx("X-CSRF-TOKEN", await GetCsrfToken(cookie)),
            new HttpHandler.RequestHeadersEx("Cookie", $".ROBLOSECURITY={cookie}")
        });

        //Console.WriteLine("GetAuthTicket: " + await request.Content.ReadAsStringAsync());
        
        return request.Headers.GetValues("rbx-authentication-ticket").FirstOrDefault();
    }

    public async Task<string> GetRobloxChannel() //erm?
    {
        var request =
            await _handler.SendAsync("https://clientsettings.roblox.com/v2/user-channel?binaryType=WindowsPlayer",
                HttpMethod.Get, new []
                {
                    new HttpHandler.RequestHeadersEx("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:150.0) Gecko/20100101 Firefox/150.0")
                });

        var response = await request.Content.ReadAsStringAsync();
        
        using var document = JsonDocument.Parse(response);

        var channelName = document.RootElement.GetProperty("channelName").ToString();

        return channelName;
    }
    
    public async Task<string> GetCurrentRobloxChannel()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\ROBLOX Corporation\Environments\RobloxPlayer\Channel");
        var currentChannelName = key?.GetValue("www.roblox.com")?.ToString();

        return string.IsNullOrEmpty(currentChannelName) ? "NO_CHANNEL_SELECTED" : currentChannelName;
    }
    
    public Task EditRobloxChannel(string channelName)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\ROBLOX Corporation\Environments\RobloxPlayer\Channel");

        key.SetValue("www.roblox.com", channelName, RegistryValueKind.String);

        return Task.CompletedTask;
    }
    
    public async Task<bool> IsChannelPrivate(string channelName)
    {
        var targetChannel = channelName.ToLower() == "production" ? "live" : channelName;
        
        try
        {
            var request =
                await _handler.SendAsync(
                    $"https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer/channel/{targetChannel}/",
                    HttpMethod.Get);

            var response = await request.Content.ReadAsStringAsync();
            Console.WriteLine(response);
            
            return !request.IsSuccessStatusCode;
        }
        catch(Exception)
        {
            return true;
        }
    }

    
    
    public async Task<string> GetChannelVersion(string channelName)
    {
        bool isDefault = String.Compare(channelName, "production", StringComparison.OrdinalIgnoreCase) == 0;

        var url = "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer";
        
        if(!isDefault)
            url += $"/channel/{channelName}";

        try
        {
            var request = await _handler.SendAsync(url, HttpMethod.Get);
            var response = await request.Content.ReadAsStringAsync();
            Console.WriteLine(response);

            var doc = JsonDocument.Parse(response);
            var version = doc.RootElement.GetProperty("clientVersionUpload").ToString();

            return version;
        }
        catch (Exception e)
        {
            return "NOT-FOUND";
        }
    }

    public async Task DownloadRobloxAsync(string version, bool isDefault, string path)
    {
        var baseUrl = "https://setup.rbxcdn.com";
    
        if (!isDefault)
            baseUrl += "/channel/common";

        await _handler.DownloadFileAsync($"{baseUrl}/{version}-RobloxApp.zip", path);
    }

    private async Task<string> GetUserThumbnail(string userId)
    {
        var request = await _handler.SendAsync($"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=false", HttpMethod.Get);
        var response = await request.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(response);
        
        var image = doc.RootElement
            .GetProperty("data")[0]
            .GetProperty("imageUrl")
            .GetString();

        return image ?? "No_Image_Found";
    }

    private async Task<Dictionary<string, List<string>>> GetPlayerTokens(string gameId, string cookie)
    {
        var serverMap = new Dictionary<string, List<string>>();
        var nextPageCursor = "";

        do
        {
            string url = $"https://games.roblox.com/v1/games/{gameId}/servers/Public?limit=100" + 
                         (string.IsNullOrEmpty(nextPageCursor) ? "" : $"&cursor={nextPageCursor}");
            
            Console.WriteLine(cookie);
            
            var request = await _handler.SendAsync(url, HttpMethod.Get, [
                new HttpHandler.RequestHeadersEx("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:150.0) Gecko/20100101 Firefox/150.0"),
                new HttpHandler.RequestHeadersEx("Cookie", $".ROBLOSECURITY={cookie}")
            ]);
            
            var response = await request.Content.ReadAsStringAsync();
            Console.WriteLine(response);
            
            if (!request.IsSuccessStatusCode || string.IsNullOrEmpty(response) || response.Contains("{}"))
            {
                Console.WriteLine("API Error: " + response);
                break; 
            }
            
            using var doc = JsonDocument.Parse(response);
            
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var servers))
            {
                foreach (var server in servers.EnumerateArray())
                {
                    var id = server.GetProperty("id").GetString() ?? "";

                    var tokenList = new List<string>();
                    
                    if (server.TryGetProperty("playerTokens", out var pTokens))
                    {
                        tokenList.AddRange(pTokens.EnumerateArray().Select(t => t.GetString() ?? ""));
                    }
                    
                    if (server.TryGetProperty("players", out var playersArray))
                    {
                        foreach (var p in playersArray.EnumerateArray())
                        {
                            if (!p.TryGetProperty("playerToken", out var t)) continue;
                            string tokenValue = t.GetString() ?? "";
                            if (!string.IsNullOrEmpty(tokenValue) && !tokenList.Contains(tokenValue))
                            {
                                tokenList.Add(tokenValue);
                            }
                        }
                    }

                    if (tokenList.Count > 0)
                    {
                        serverMap.TryAdd(id, tokenList);
                    }
                }
            }

            nextPageCursor = root.TryGetProperty("nextPageCursor", out var next) && next.ValueKind == JsonValueKind.String 
                ? next.GetString() 
                : null;

        } while (nextPageCursor != null);

        return serverMap;
    }

    private async Task<string> ScrapeServerInfo(IEnumerable<object> batchItems)
    {
        var jsonPayload = JsonSerializer.Serialize(batchItems);
        
        var request = await _handler.SendAsync("https://thumbnails.roblox.com/v1/batch", HttpMethod.Post, jsonPayload, [
            new HttpHandler.RequestHeadersEx("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:150.0) Gecko/20100101 Firefox/150.0"),
            new HttpHandler.RequestHeadersEx("Referer", "https://create.roblox.com/"),
            new HttpHandler.RequestHeadersEx("Origin", "https://create.roblox.com")
        ]);
        
        var response = await request.Content.ReadAsStringAsync();
        
        return !request.IsSuccessStatusCode ? "{\"data\":[]}" : response;
    }

    public async Task<string?> FindPlayerServer(string targetPlayerId, string gameId, string cookie)
    {
        var targetThumbnail = await GetUserThumbnail(targetPlayerId);
        if (targetThumbnail == "No_Image_Found" || !targetThumbnail.Contains('/'))
        {
            SnackbarHelper.ShowWarning("Warning", $"No thumbnail found of player with id: {targetPlayerId}.");
            return null;
        }
        
        var parts = targetThumbnail.Split('/');
        if (parts.Length < 4) return null;
        
        var targetHash = parts[3];
        var serverMap = await GetPlayerTokens(gameId, cookie);
        
        if (serverMap.Count == 0)
        {
            SnackbarHelper.ShowWarning("Warning", "No servers found for this game.");
            return null;
        } 
        
        var flatList = serverMap
            .SelectMany(kvp => kvp.Value.Select(token => new { ServerId = kvp.Key, Token = token }))
            .ToList();
        
        for (int i = 0; i < flatList.Count; i += 100)
        {
            var chunk = flatList.Skip(i).Take(100).ToList();
            
            var batchItems = chunk.Select(item => new
            {
                requestId = item.ServerId,
                token = item.Token,
                type = "AvatarHeadShot",
                size = "150x150",
                format = "Png",
                isCircular = false
            });
                
            var responseJson = await ScrapeServerInfo(batchItems);
            Console.WriteLine(responseJson);
            using var doc = JsonDocument.Parse(responseJson);

            if (doc.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var entry in dataArray.EnumerateArray())
                {
                    string? imageUrl = entry.GetProperty("imageUrl").GetString();

                    if (imageUrl == null || !imageUrl.Contains(targetHash)) continue;
                    SnackbarHelper.ShowSuccess("Success", "Player Found!");
                    return entry.GetProperty("requestId").GetString();
                }
            }
            
            await Task.Delay(200);
        }
        
        SnackbarHelper.ShowWarning("Warning", "Player is not found, or not playing a game!");
        return null;
    }

    public async Task JoinServerThroughId(string cookie, string placeId, string serverId)
    {
        var authTicket = await GetAuthenticationTicketAsync(cookie);
        
        var currentVersion = await GetRobloxVersionAsync();

        var robloxPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");
        
        var robloxExe = Path.Combine(
            robloxPath,
            "Versions",
            currentVersion,
            "RobloxPlayerBeta.exe");
        
        Process.Start(new ProcessStartInfo
        {
            FileName = robloxExe,
            Arguments = $"--app -t {authTicket} -j https://www.roblox.com/Game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={DateNow()}&placeId={placeId}&isPlayTogetherGame=false&referredByPlayerId=0&joinAttemptId={serverId}&joinAttemptOrigin=PlayButton -LaunchExp InApp"
        });
    }
    
    long DateNow()
    {
        return ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();
    }
}