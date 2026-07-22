using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Shinystrap.Handlers.Roblox;
using Shinystrap.Handlers.Shinystrap;
using Shinystrap.Handlers.Web;

namespace Shinystrap;

public partial class App : Application
{
    public const string Version = "v1.0.5";

    private readonly HttpHandler _handler = new();
    private readonly RobloxApi _api = new();
    
    private CancellationTokenSource? _cancellation;
    private CancellationTokenSource? _updateCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AccountManaging.Instance.LoadAccounts();

        if (Shinystrap.Properties.Settings.Default.RbxAutoUpdate)
        {
            StartRbxAutoUpdate();
        }

        if (Shinystrap.Properties.Settings.Default.ShinyAutoUpdate)
        {
            StartAppAutoUpdate();
        }

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

    private async Task StartUpdateLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await CheckForUpdatesAsync(true);
                await Task.Delay(TimeSpan.FromMinutes(10), token);
            }
            catch (Exception exception)
            {
                Shinystrap.Properties.Settings.Default.ShinyAutoUpdate = false;
                Shinystrap.Properties.Settings.Default.Save();
                StopAppAutoUpdate();

                SnackbarHelper.ShowError("Shinystrap - Error", $"{exception.Message}");

                break;
            }
        }
    }

    public async Task CheckForUpdatesAsync(bool silent = false)
    {
        var appVersion =
            await _handler.GetStringAsync(
                "https://raw.githubusercontent.com/Trollicus/Shinystrap/main/version.txt");

        if (string.Equals(Version, appVersion.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            if (!silent)
                SnackbarHelper.ShowSuccess("Shinystrap", "You're already on the latest version!");

            return;
        }

        SnackbarHelper.ShowInfo("Shinystrap", "New Shiny version detected, updating!");

        var appDir = AppContext.BaseDirectory;
        var tempRoot = Path.Combine(Path.GetTempPath(), "Shinystrap", Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(tempRoot, "update.zip");
        var extractPath = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(extractPath);

        var updateUrl = "https://github.com/Trollicus/Shinystrap/releases/latest/download/Shinystrap.zip";
        await _handler.DownloadFileAsync(updateUrl, zipPath);

        await ZipFile.ExtractToDirectoryAsync(zipPath, extractPath, overwriteFiles: true);

        var updaterScript = Path.Combine(tempRoot, "update.bat");
        var appExe = Path.Combine(appDir, "Shinystrap.exe");

        var script = $"""
                      @echo off
                      setlocal
                      cd /d "{appDir}"

                      :waitloop
                      timeout /t 2 /nobreak >nul
                      tasklist /fi "imagename eq Shinystrap.exe" | find /i "Shinystrap.exe" >nul
                      if not errorlevel 1 goto waitloop

                      del /f /q "{appDir}\*.*" >nul 2>&1
                      for /d %%D in ("{appDir}\*") do rmdir /s /q "%%D" >nul 2>&1

                      xcopy /y /e /i "{extractPath}\*" "{appDir}\" >nul

                      powershell -NoProfile -WindowStyle Hidden -Command "Start-Process -FilePath '{appExe}' -WorkingDirectory '{appDir}'"

                      timeout /t 2 /nobreak >nul

                      rmdir /s /q "{extractPath}" >nul 2>&1
                      del /f /q "{zipPath}" >nul 2>&1
                      rmdir /s /q "{tempRoot}" >nul 2>&1

                      exit /b 0
                      """;

        await File.WriteAllTextAsync(updaterScript, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = updaterScript,
            UseShellExecute = true,
            WorkingDirectory = tempRoot
        });

        Environment.Exit(0);
    }

    private async Task RbxAutoUpdate(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                Console.Write("test1");
                if (await _api.CheckForUpdatesAsync())
                {
                    SnackbarHelper.ShowInfo("New Version Detected", "New Version Detected, Updating!",
                        TimeSpan.FromSeconds(5));

                    var processes = Process.GetProcessesByName("RobloxPlayerBeta");
                    try
                    {
                        if (processes.Length > 0)
                        {
                            SnackbarHelper.ShowError("Error", "Please close Roblox before Shinystrap updates.");

                            await Task.Delay(TimeSpan.FromMinutes(10), token);
                            continue;
                        }
                    }
                    finally
                    {
                        foreach (var process in processes)
                            process.Dispose();
                    }

                    try
                    {
                        var initialization = new Initialization();

                        var currentVersion = await _api.GetRobloxVersionAsync();
                        var defaultPath = Shinystrap.Properties.Settings.Default.DefaultInstalledPath;

                        await initialization.InitializeAsync(currentVersion, defaultPath);
                        await initialization.SetRobloxProtocol();

                        await _api.SetRegistryRobloxVersion(currentVersion);

                        SnackbarHelper.ShowSuccess(
                            "Shinystrap",
                            "Roblox updated successfully!"
                        );
                    }
                    catch (Exception ex)
                    {
                        SnackbarHelper.ShowError("Error - Show Dev", ex.Message);
                    }
                }

                await Task.Delay(TimeSpan.FromMinutes(10), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Shinystrap.Properties.Settings.Default.RbxAutoUpdate = false;
                    _ = Task.Run(() => Shinystrap.Properties.Settings.Default.Save(), token);
                    StopRbxAutoUpdate();
                });

                SnackbarHelper.ShowError("Shinystrap - Error", ex.Message);
                break;
            }
        }
    }

    public void StartAppAutoUpdate()
    {
        if (_updateCts != null)
            return;

        _updateCts = new CancellationTokenSource();
        _ = StartUpdateLoop(_updateCts.Token);
    }

    public void StopAppAutoUpdate()
    {
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _updateCts = null;
    }

    public void StartRbxAutoUpdate()
    {
        if (_cancellation != null)
            return;

        _cancellation = new CancellationTokenSource();
        _ = RbxAutoUpdate(_cancellation.Token);
    }

    public void StopRbxAutoUpdate()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
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
        var currentVersion = await _api.GetRobloxVersionAsync();

        if (await _api.CheckForUpdatesAsync())
        {
            MessageBox.Show("Outdated Roblox, please update before launching!");
            return;
        }

        if (args.Length > 0 && args[0].Contains("install-roblox", StringComparison.OrdinalIgnoreCase))
        {
            var processes = Process.GetProcessesByName("RobloxPlayerBeta");
            if (processes.Length > 0)
            {
                MessageBox.Show("Error", "Please close Roblox before Shinystrap updates.");
                return;
            }

            try
            {
                var initialization = new Initialization();
                var defaultPath = Shinystrap.Properties.Settings.Default.DefaultInstalledPath;

                await initialization.InitializeAsync(currentVersion, defaultPath);
                await initialization.SetRobloxProtocol();

                MessageBox.Show($"Successfully Installed Roblox {currentVersion}!");
            }
            catch (Exception ex)
            {
                SnackbarHelper.ShowError("Error - Show Dev", ex.Message);
            }

            return;
        }

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
            return;
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
            Shinystrap.Properties.Settings.Default.DefaultInstalledPath,
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