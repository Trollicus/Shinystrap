using CommunityToolkit.WinUI.Notifications;
using Shinystrap.Handlers.Roblox;
using Shinystrap.Handlers.Shinystrap;
using Shinystrap.Handlers.Web;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace Shinystrap.Pages
{
    public partial class Addons
    {
        private readonly RobloxApi _api = new();
        private readonly HttpHandler _httpHandler = new();
        
        private readonly string _robloxSettingsFile =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "GlobalBasicSettings_13.xml");

        private CancellationTokenSource? _cts;
        private Mutex? _mutex1;
        private Mutex? _mutex2;
        private string? _lastNotifiedIp;
        
        
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
                    Content = "This action is irreversible. Roblox will be completely reinstalled.\n\nAre you sure you want to continue?",
                    PrimaryButtonText = "Yes, Reinstall",
                    CloseButtonText = "Cancel"
                };

                var result = await dialog.ShowDialogAsync();
                if (result != Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    return;
                }

                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var robloxInstallmentPath = Path.Combine(appDataPath, "Roblox");

                try
                {
                    if (Directory.Exists(robloxInstallmentPath))
                    {
                        Directory.Delete(robloxInstallmentPath, true);
                    }
                }
                catch (Exception)
                {
                    SnackbarHelper.ShowError("Error", "Error deleting Roblox Message");
                }

                var rbxVersion = await _api.GetRobloxVersionAsync();
                var installerPath = Path.Combine(Directory.GetCurrentDirectory(), "RobloxPlayerInstaller.exe");

                await _httpHandler.DownloadFileAsync(
                    $"https://setup.rbxcdn.com/{rbxVersion}-RobloxPlayerInstaller.exe",
                    installerPath);

                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SnackbarHelper.ShowError("Failed to install Roblox", ex.Message);
            }
        }

        private void TrackServerLocation_OnChecked(object sender, RoutedEventArgs e)
        {
            if (_cts is not null)
            {
                return;
            }
            
            _cts = new CancellationTokenSource();
            _ = WatchLogsAsync(_cts.Token);
        }

        private async Task WatchLogsAsync(CancellationToken token)
        {
            var robloxLogsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox",
                "logs");

            var ipRegex = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b");

            while (!token.IsCancellationRequested)
            {
                var latestLog = Directory.GetFiles(robloxLogsPath)
                    .OrderByDescending(File.GetLastWriteTime)
                    .FirstOrDefault();

                if (latestLog is null)
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
                    
                    _lastNotifiedIp = ip;
                    await OnServerIpDetectedAsync(ip);
                }
            }
        }

        private async Task OnServerIpDetectedAsync(string ip)
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
        
        private void TrackServerLocation_OnUnchecked(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _lastNotifiedIp = null;
        }

        private void RbxMutex_OnChecked(object sender, RoutedEventArgs e)
        {
            try
            {
                _mutex1 = new Mutex(true, "ROBLOX_singletonMutex", out bool created1);
                _mutex2 = new Mutex(true, "ROBLOX_singletonEvent", out bool created2);

                if (created1 && created2) return;
                SnackbarHelper.ShowError("Error", "Make sure roblox is closed first!");
                Dispatcher.BeginInvoke(() => MutexSwitch.IsChecked = false);
            }
            catch (Exception)
            {
                CleanupMutexes();

                SnackbarHelper.ShowError("Failed to initialize", "Close all Roblox windows first!");

                Dispatcher.BeginInvoke(() => MutexSwitch.IsChecked = false);
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

        private async void Addons_OnLoaded(object sender, RoutedEventArgs e)
        {
            var channel = await _api.GetCurrentRobloxChannel();
            CurrentChannel.Text = $"Current Channel: {channel}";

            CurrentFPS.Text = GetFramerateCap().ToString();

            if (!Properties.Settings.Default.RuleExists)
            {
                ExistingRuleExpander.Visibility = Visibility.Collapsed;
            }
            if(Properties.Settings.Default.RuleExists && !string.IsNullOrEmpty(Properties.Settings.Default.RuleName))
            {
                ExistingRuleName.Text = Properties.Settings.Default.RuleName;
                
                var exitingIps = FetchRuleIps();
                ExistingIps.Text = string.Join("\n", exitingIps.Split(','));
            }
        }

        private async void ChangeChanel_OnClick(object sender, RoutedEventArgs e)
        {
            if (WrittenChannel.Text != "private version" && WrittenChannel.Text.Contains("version"))
            {
                //there has to be better way of doing allat lol
                var robloxPath =
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions",  WrittenChannel.Text);

                var robloxPath2 =
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
                
                var sourceFolder = new DirectoryInfo(robloxPath2)
                    .GetDirectories()
                    .Where(d => d.Name != WrittenChannel.Text)
                    .OrderByDescending(d => d.LastWriteTime)
                    .FirstOrDefault();
                
                if (Directory.Exists(robloxPath))
                {
                    SnackbarHelper.ShowWarning("Warning", "The roblox version already exists!");
                    return;
                }
                
                Directory.CreateDirectory(robloxPath);
                
                bool isDefault = String.Compare(SetChannel.Text, "production", StringComparison.OrdinalIgnoreCase) == 0;

                var zipPath = robloxPath + "\\RobloxApp.zip";
                
                await _api.DownloadRobloxAsync(WrittenChannel.Text, isDefault, zipPath);
                await ZipFile.ExtractToDirectoryAsync(zipPath, robloxPath, overwriteFiles: true);
                File.Delete(zipPath);
                
                if (sourceFolder != null)
                {
                    foreach (string file in Directory.EnumerateFiles(sourceFolder.FullName, "*", SearchOption.AllDirectories))
                    {
                        string relativePath = Path.GetRelativePath(sourceFolder.FullName, file);
                        string destFile = Path.Combine(robloxPath, relativePath);

                        Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                        if (!File.Exists(destFile))
                            File.Copy(file, destFile);
                    }
                }
                
                await _api.EditRobloxChannel(SetChannel.Text);
            }
            else
            {
                SnackbarHelper.ShowWarning("Warning", "Invalid version!");
            }
        }

        private async void SnipePlayer_OnClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SnipeUsername.Text) || string.IsNullOrEmpty(SnipePlaceId.Text))
            {
                SnackbarHelper.ShowError("Error", "Please enter a valid username and/or place id");
                return;
            }
            
            if (!long.TryParse(SnipePlaceId.Text, out _) || !long.TryParse(SnipeUsername.Text, out _))
            {
                SnackbarHelper.ShowError("Error", "Place and/or User ID must be a number.");
                return;
            }

            var biscuit = RobloxManager.RobloxBiscuit;
            if (string.IsNullOrEmpty(biscuit))
            {
                SnackbarHelper.ShowWarning("Warning", "Please login first in GameHistory before using this!");
                return;
            }
            
            SnipePlayerBtn.IsEnabled = false;
            
            var serverId = await _api.FindPlayerServer(SnipeUsername.Text, SnipePlaceId.Text, biscuit);
            
            if (!string.IsNullOrEmpty(serverId))
            {
                await _api.JoinServerThroughId(biscuit, SnipePlaceId.Text, serverId);
            }
            
            SnipePlayerBtn.IsEnabled = true;
        }
        
        private CancellationTokenSource _cancellationTokenSource = new();

        private async void SetChannel_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (WrittenChannel == null || SetChannel == null) return;
            
            await _cancellationTokenSource.CancelAsync();
            _cancellationTokenSource.Dispose(); 

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            try
            {
                await Task.Delay(200, token);
                
                bool isPrivate = await _api.IsChannelPrivate(SetChannel.Text);
                var channelName = await _api.GetChannelVersion(SetChannel.Text);
                
                if (!token.IsCancellationRequested)
                {
                    WrittenChannel.Text = isPrivate ? "private version" : channelName;
                }
            }
            catch (Exception ex)
            {
                // ignored
            }
        }

        private void CreateFirewallRule_OnClick(object sender, RoutedEventArgs e)
        {
            if (Properties.Settings.Default.RuleExists)
            {
                SnackbarHelper.ShowError("Error", "A rule already exists!");
                return;
            }
            
            if (string.IsNullOrWhiteSpace(IpList.Text) || string.IsNullOrWhiteSpace(RuleName.Text)) return;

            string ips = IpList.Text
                .Split('\n')
                .Select(ip => ip.Trim())
                .Where(ip => !string.IsNullOrEmpty(ip))
                .Aggregate((a, b) => $"{a},{b}");
            
            Console.WriteLine(ips.Trim());
            
           Process.Start(new ProcessStartInfo
           {
                FileName = "netsh",
                Arguments =
                    $"advfirewall firewall add rule name=\"{RuleName.Text}\" dir=out action=block remoteip={ips}",
                Verb = "runas",
                UseShellExecute = true
           });
           
            Properties.Settings.Default.RuleName = RuleName.Text;
            ExistingRuleName.Text = Properties.Settings.Default.RuleName;
            Properties.Settings.Default.RuleExists = true;
            
            _ = Task.Run(() => Properties.Settings.Default.Save());
            
            ExistingRuleExpander.Visibility = Visibility.Visible;

            Task.Delay(500);
            var exitingIps = FetchRuleIps();
            ExistingIps.Text = string.Join("\n", exitingIps.Split(','));
            
            SnackbarHelper.ShowInfo("Success", "Successfully created firewall rule!");
        }

        private void DeleteRule_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall delete rule name=\"{Properties.Settings.Default.RuleName}\"",
                Verb = "runas",
                UseShellExecute = true
            });
            
            Properties.Settings.Default.RuleName = "";
            Properties.Settings.Default.RuleExists = false;
            ExistingRuleExpander.Visibility = Visibility.Hidden;
            _ = Task.Run(() => Properties.Settings.Default.Save());
            
            NavigationService?.Refresh();
            
            SnackbarHelper.ShowInfo("Success", "Successfully DELETED firewall rule!");
        }

        private string FetchRuleIps()
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall show rule name=\"{Properties.Settings.Default.RuleName}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            
            // Parse the RemoteIP line
            string ips = output
                .Split('\n')
                .FirstOrDefault(line => line.TrimStart().StartsWith("RemoteIP:"))
                ?.Split(':')[1]
                .Trim() ?? "";

            return ips;
        }

        private void SaveRule_Click(object sender, RoutedEventArgs e)
        {
            string ips = ExistingIps.Text
                .Split('\n')
                .Select(ip => ip.Trim())
                .Where(ip => !string.IsNullOrEmpty(ip))
                .Aggregate((a, b) => $"{a},{b}");

            Process.Start(new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall set rule name=\"{Properties.Settings.Default.RuleName}\" new remoteip={ips}",
                Verb = "runas",
                UseShellExecute = true
            });
            
            SnackbarHelper.ShowInfo("Success", "Successfully SAVED firewall rule!");
        }

        private void SaveFPSLimit_OnClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SetFPSValue.Text))
            {
                SnackbarHelper.ShowWarning("Warning", "Value cannot be null or empty!");
                return;
            }
            
            File.SetAttributes(_robloxSettingsFile, FileAttributes.Normal);

            var doc = XDocument.Load(_robloxSettingsFile);
            var element = doc.Descendants("int")
                .FirstOrDefault(e => (string)e.Attribute("name") == "FramerateCap");

            if (element != null)
                element.Value = SetFPSValue.ToString() ?? "9999";

            doc.Save(_robloxSettingsFile);

            // Set back to readonly
            File.SetAttributes(_robloxSettingsFile, FileAttributes.ReadOnly);
        }

        private int GetFramerateCap()
        {
            var doc = XDocument.Load(_robloxSettingsFile);
            var value = doc.Descendants("int")
                .FirstOrDefault(e => (string)e.Attribute("name") == "FramerateCap")
                ?.Value;

            return int.TryParse(value, out int cap) ? cap : 0;
        }
    }
}