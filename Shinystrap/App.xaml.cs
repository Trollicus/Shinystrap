using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Web;
using System.Windows;
using Shinystrap.Handlers.Roblox;

namespace Shinystrap;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length == 0)
        {
            return;
        }

        _ = HandleProtocolLaunchAsync(e.Args)
            .ContinueWith(_ => Dispatcher.Invoke(Shutdown));
    }

    private async Task HandleProtocolLaunchAsync(string[] args)
    {
        var api = new RobloxApi();
        var currentVersion = await api.GetRobloxVersionAsync();

        var robloxPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");

        var decodedArgs = WebUtility.UrlDecode(args[0]).Trim();

        var parsedArgs = decodedArgs
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(':', 2))
            .Where(part => part.Length == 2)
            .ToDictionary(part => part[0], part => part[1]);

        if (!parsedArgs.TryGetValue("placelauncherurl", out var placeUrl) ||
            !parsedArgs.TryGetValue("gameinfo", out var gameInfo))
        {
            MessageBox.Show("Invalid Roblox protocol arguments, pls contact admin/mod");
            throw new InvalidOperationException("Invalid Roblox protocol arguments.");
        }

        var spoofBrowserTracker = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);

        var uri = new Uri(placeUrl);
        var query = HttpUtility.ParseQueryString(uri.Query);
        query["browserTrackerId"] = spoofBrowserTracker.ToString();

        var updatedUrl = new UriBuilder(uri)
        {
            Query = query.ToString()
        }.ToString();

        var robloxExe = Path.Combine(
            robloxPath,
            "Versions",
            currentVersion,
            "RobloxPlayerBeta.exe");

        Process.Start(new ProcessStartInfo
        {
            FileName = robloxExe,
            Arguments = $"--app -t {gameInfo} -j {updatedUrl} -LaunchExp InApp"
        });
    }
}