using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;
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
        var request = await _handler.SendAsync("https://clientsettingscdn.roblox.com/v1/client-version/WindowsPlayer", HttpMethod.Get);
        request.EnsureSuccessStatusCode();

        var response = await request.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(response);

        if (!document.RootElement.TryGetProperty("clientVersionUpload", out var versionElement))
            throw new InvalidOperationException(
                "Roblox version response did not contain a valid clientVersionUpload value.");
        var version = versionElement.GetString();

        return !string.IsNullOrWhiteSpace(version) ? version : throw new InvalidOperationException("Roblox version response did not contain a valid clientVersionUpload value.");
    }
}