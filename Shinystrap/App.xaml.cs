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

        if (e.Args.Length > 0)
        {
            HandleProtocolAsync(e.Args)
                .ContinueWith(_ => Dispatcher.Invoke(Current.Shutdown));
        }
    }

    private async Task HandleProtocolAsync(string[] args)
    {
        var api = new RobloxApi();

        var currentVersion = await api.GetRobloxVersionAsync();
        var robloxPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");

        if (args.Length > 0)
        {
            var decodedArgs = WebUtility.UrlDecode(args[0]).Trim();

            var coolArgs = decodedArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split(':', 2))
                .Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]);

            var spoofBrowserTracker = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);

            var placeUrl = coolArgs["placelauncherurl"];

            var uri = new Uri(placeUrl);
            var query = HttpUtility.ParseQueryString(uri.Query);

            query["browserTrackerId"] = spoofBrowserTracker.ToString();

            var builder = new UriBuilder(uri)
            {
                Query = query.ToString()
            };

            var updatedUrl = builder.ToString();

            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(robloxPath +
                                        $@"\Versions\{currentVersion}\RobloxPlayerBeta.exe"),
                Arguments =
                    $"--app -t {coolArgs["gameinfo"]} -j {updatedUrl} -LaunchExp InApp"
            });
        }
    }
}