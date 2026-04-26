using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    public async Task<string> GetCurrentRobloxChannel()
    {
        var request =
            await _handler.SendAsync("https://clientsettings.roblox.com/v2/user-channel?binaryType=WindowsPlayer",
                HttpMethod.Get);

        var response = await request.Content.ReadAsStringAsync();
        
        using var document = JsonDocument.Parse(response);

        var channelName = document.RootElement.GetProperty("channelName").ToString();

        return channelName;
    }
}