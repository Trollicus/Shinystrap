using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
            var appVersion = await _handler.GetStringAsync("https://raw.githubusercontent.com/Trollicus/Shinystrap/main/version.txt");
            
            if (string.Equals(_version, appVersion.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                SnackbarHelper.ShowSuccess("Shinystrap", "You're already on the latest version!");
                return;
            }

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

        private CancellationTokenSource _updateCts = null!;

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
