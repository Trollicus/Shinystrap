using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;
using Shinystrap.Handlers.Web;

namespace Shinystrap.Handlers.Roblox;

public class RobloxApi
{
    private readonly HttpHandler _handler = new();
    
    public async Task<bool> CheckForUpdates()
    {
        var request = await _handler.SendAsync("https://clientsettingscdn.roblox.com/v1/client-version/WindowsPlayer",
            HttpMethod.Get);
        
        var response = await request.Content.ReadAsStringAsync();
        
        using var doc = JsonDocument.Parse(response);

        var version = doc.RootElement.GetProperty("clientVersionUpload").GetString();

        var path = @"Software\Classes\roblox-player\shell\open\command";

        using var key = Registry.CurrentUser.OpenSubKey(path);
        var currentVersion = key?.GetValue("version")?.ToString();

        return currentVersion != version;
    }

    public async Task<string?> GetRobloxVersionAsync()
    {
        var request =
            await _handler.SendAsync("https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer",
                HttpMethod.Get);

        var response = await request.Content.ReadAsStringAsync();
        
        var document = JsonDocument.Parse(response);
        
        if (document.RootElement.TryGetProperty("clientVersionUpload", out JsonElement clientVersionUpload))
        {
            return clientVersionUpload.GetString();
        }
        throw new Exception("NO ROBLOX VERSION BRO? ~ TROLLICUS");
    }
}