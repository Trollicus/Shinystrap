using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Shinystrap.Handlers.Roblox;

namespace Shinystrap;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnGlobalPreviewMouseWheel),
            true);
        
        if (e.Args.Length == 0)
        {
            return;
        }

        _ = HandleProtocolLaunchAsync(e.Args)
            .ContinueWith(_ => Dispatcher.Invoke(Shutdown));
    }
    
    //Thanks to JetBrains Rider AI on this one lol
    private static void OnGlobalPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindAncestor<TextBoxBase>(source) is not null)
        {
            return;
        }

        const double scrollMultiplier = 1.0;

        var current = source;
        ScrollViewer? targetScrollViewer = null;

        while (current is not null)
        {
            if (current is ScrollViewer sv && sv.ScrollableHeight > 0)
            {
                targetScrollViewer = sv;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        if (targetScrollViewer is null)
        {
            return;
        }

        var delta = e.Delta * scrollMultiplier;
        var newOffset = targetScrollViewer.VerticalOffset - delta;

        if (newOffset < 0)
        {
            newOffset = 0;
        }
        else if (newOffset > targetScrollViewer.ScrollableHeight)
        {
            newOffset = targetScrollViewer.ScrollableHeight;
        }

        targetScrollViewer.ScrollToVerticalOffset(newOffset);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
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