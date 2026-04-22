using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.WinUI.Notifications;
using Shinystrap.Handlers.Roblox;
using Shinystrap.Handlers.Shinystrap;
using Shinystrap.Handlers.Web;

namespace Shinystrap.Pages
{
    public partial class Addons : Page
    {
        private readonly RobloxApi _api = new();
        private readonly HttpHandler _httpHandler = new();

        public Addons()
        {
            InitializeComponent();
        }

        private async void ReInstall_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "⚠️ Caution",
                    Content =
                        "This action is irreversible. Roblox will be completely reinstalled.\n\nAre you sure you want to continue?",
                    PrimaryButtonText = "Yes, Reinstall",
                    CloseButtonText = "Cancel",
                };

                var result = await dialog.ShowDialogAsync();

                if (result != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var robloxInstallmentPath = Path.Combine(appDataPath, "Roblox");

                try
                {
                    if (Directory.Exists(robloxInstallmentPath))
                    {
                        Directory.Delete(robloxInstallmentPath, true);
                        Console.WriteLine("Roblox folder deleted successfully.");
                    }
                    else
                    {
                        Console.WriteLine("No Roblox folder found.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }

                var rbxVersion = await _api.GetRobloxVersionAsync();

                await _httpHandler.DownloadFileAsync($"https://setup.rbxcdn.com/{rbxVersion}-RobloxPlayerInstaller.exe",
                    Directory.GetCurrentDirectory() + "\\RobloxPlayerInstaller.exe");
                Process.Start(Directory.GetCurrentDirectory() + "\\RobloxPlayerInstaller.exe");
            }
            catch (Exception ex)
            {
                SnackbarHelper.ShowError("Failed to install Roblox", ex.Message);
            }
        }

        private CancellationTokenSource? _cts;

        private void TrackServerLocation_OnChecked(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => WatchLogs(_cts.Token));
        }

        private string? _lastNotifiedIp;

        private async Task WatchLogs(CancellationToken token)
        {
            var robloxLogsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox\\logs");

            var ipRegex = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b");

            while (!token.IsCancellationRequested)
            {
                var latestLog = Directory.GetFiles(robloxLogsPath)
                    .OrderByDescending(File.GetLastWriteTime)
                    .FirstOrDefault();

                if (latestLog == null)
                {
                    await Task.Delay(1000, token);
                    continue;
                }

                await using var fs = new FileStream(latestLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);

                fs.Seek(0, SeekOrigin.End);

                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token);

                    if (line == null)
                    {
                        await Task.Delay(200, token);

                        var currentLatest = Directory.GetFiles(robloxLogsPath)
                            .OrderByDescending(File.GetLastWriteTime)
                            .First();

                        if (latestLog != currentLatest)
                            break;

                        continue;
                    }

                    if (line.Contains("setStage: (stage:None)"))
                    {
                        _lastNotifiedIp = null;
                        continue;
                    }

                    if (!line.Contains("Connecting") && !line.Contains("Address"))
                        continue;

                    var match = ipRegex.Match(line);
                    if (!match.Success) continue;

                    var ip = match.Value;

                    if (ip == _lastNotifiedIp) continue;

                    if (!IsValidPublicIp(ip)) continue;

                    _lastNotifiedIp = ip;
                    OnServerIpDetected(ip);
                }
            }
        }

        private async void OnServerIpDetected(string ip)
        {
            try
            {
                ToastNotificationManagerCompat.History.Clear();

                var json = await _httpHandler.SendAsync($"https://ipinfo.io/{ip}/json", HttpMethod.Get);
                var response = await json.Content.ReadAsStringAsync();

                var doc = JsonDocument.Parse(response);
                var city = doc.RootElement.GetProperty("city").ToString();
                var region = doc.RootElement.GetProperty("region").ToString();

                new ToastContentBuilder()
                    .AddText("Connected to server")
                    .AddText($"Location: {city}, {region}")
                    .AddAttributionText($"IP: {ip}")
                    .Show();
            }
            catch (Exception exception)
            {
                SnackbarHelper.ShowError("Failed to connect to server", exception.Message);
            }
        }

        private bool IsValidPublicIp(string ip)
        {
            return !(ip.StartsWith("127.") ||
                     ip.StartsWith("10.") ||
                     ip.StartsWith("192.168.") ||
                     ip.StartsWith("172."));
        }

        private void TrackServerLocation_OnUnchecked(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private Mutex? _mutex1;
        private Mutex? _mutex2;
        //TODO: Idea show all instances of boblox stolen idea from this guy: https://github.com/Avaluate/MultipleRobloxInstances

        private void RbxMutex_OnChecked(object sender, RoutedEventArgs e)
        {
            try
            {
                _mutex1 = new Mutex(true, "ROBLOX_singletonMutex", out bool created1);
                _mutex2 = new Mutex(true, "ROBLOX_singletonEvent", out bool created2);

                if (created1 && created2) return;
                SnackbarHelper.ShowError("Error", "Make sure roblox is closed first!");
                Dispatcher.BeginInvoke(() => { MutexSwitch.IsChecked = false; });
            }
            catch (Exception)
            {
                CleanupMutexes();

                SnackbarHelper.ShowError("Failed to initialize", "Close all Roblox windows first!");

                Dispatcher.BeginInvoke(() => { MutexSwitch.IsChecked = false; });
            }
        }

        private void RbxMutex_OnUnchecked(object sender, RoutedEventArgs e)
        {
            CleanupMutexes();
        }

        private void CleanupMutexes()
        {
            _mutex1?.Close();
            _mutex1 = null;

            _mutex2?.Close();
            _mutex2 = null;
        }
    }
}