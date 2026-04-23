using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Shinystrap.Handlers.Roblox;
using Shinystrap.Handlers.Shinystrap;
using Shinystrap.Handlers.Web;

namespace Shinystrap.Pages
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings
    {
        private readonly string _version = "v1.0.0";
        private readonly HttpHandler _handler = new();
        
        public Settings()
        {
            InitializeComponent();
        }

        private async void Initialize_OnClick(object sender, RoutedEventArgs e)
        {
            await SetRobloxProtocolAsync();
        }

        private Task SetRobloxProtocolAsync()
        {
            return Task.Run(() =>
                {
                    var currentProcess =
                        Environment.ProcessPath ??
                        Path.Combine(AppContext.BaseDirectory, "Shinystrap.exe");

                    var value = $"\"{currentProcess}\" \"%1\"";

                    using var key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Classes\roblox-player\shell\open\command",
                        writable: true);

                    if (key is null)
                        throw new Exception("Registry key not found.");

                    key.SetValue("", value);
                })
                .ContinueWith(_ =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        SnackbarHelper.ShowSuccess("Shinystrap", "Initialized!");
                    });
                });
        }
        

        private async void UnInstall_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var api = new RobloxApi();

                if (await api.CheckForUpdatesAsync())
                {
                    SnackbarHelper.ShowWarning(
                        "Shinystrap",
                        "Roblox version mismatch, might not work properly please update your roblox!"
                    );
                }

                var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var versionsPath = Path.Combine(basePath, "Roblox", "Versions");

                var versionFolder = await api.GetRobloxVersionAsync();

                if (string.IsNullOrWhiteSpace(versionFolder))
                {
                    versionFolder = Directory
                        .GetDirectories(versionsPath)
                        .OrderByDescending(Directory.GetLastWriteTime)
                        .FirstOrDefault();

                    if (versionFolder == null)
                        return;

                    versionFolder = Path.GetFileName(versionFolder);
                }

                var robloxExe = Path.Combine(
                    versionsPath,
                    versionFolder,
                    "RobloxPlayerBeta.exe"
                );

                var value = $"\"{robloxExe}\" \"%1\"";

                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\roblox-player\shell\open\command",
                    writable: true);

                if (key is null)
                    throw new Exception("Registry key not found.");

                key.SetValue("", value);
                
                SnackbarHelper.ShowSuccess(
                    "Shinystrap",
                    "Roblox is set as default launcher"
                );
            }
            catch (Exception exception)
            {
                SnackbarHelper.ShowError("Shinystrap - Error", $"{exception.Message}");
            }
        }

        private async void CheckUpdate_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await CheckForUpdatesAsync();
            }
            catch (Exception exception)
            {
                SnackbarHelper.ShowError("Shinystrap - Error", $"{exception.Message}");
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            //TODO: Github version link add
            var appVersion = await _handler.GetStringAsync("https://github.com/Trollicus/Shinystrap/blob/main/version.txt");

            if (_version != appVersion.Trim())
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), "Bin\\");

                Console.WriteLine(path);

                var oldPath = Path.Combine(AppContext.BaseDirectory + "Bin");
    
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }

                Console.WriteLine(AppContext.BaseDirectory + "Shinystrap.exe");

                File.Move(AppContext.BaseDirectory + "Shinystrap.exe",
                    path);


                await _handler.DownloadFileAsync("", Path.Combine(Directory.GetCurrentDirectory() + "\\Shinystrap.exe"));

                Process.Start(Directory.GetCurrentDirectory() + "\\Shinystrap.exe");
                Environment.Exit(0);
            }
        }
        
        private CancellationTokenSource _updateCts;

        private void UpdateToggle_Checked(object sender, RoutedEventArgs e)
        {
            _updateCts = new CancellationTokenSource();
            _ = StartUpdateLoop(_updateCts.Token);
        }
        
        private async Task StartUpdateLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await CheckForUpdatesAsync();
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
                catch (Exception exception)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        UpdateToggle.IsChecked = false;
                    });
                    
                    SnackbarHelper.ShowError("Shinystrap - Error", $"{exception.Message}");
                    
                    break;
                }
            }
        }

        private void UpdateToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _updateCts.Cancel();
        }
    }
}
