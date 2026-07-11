using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
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
        private const string Version = "v1.0.3";
        private readonly HttpHandler _handler = new();

        public Settings()
        {
            InitializeComponent();
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
            var appVersion =
                await _handler.GetStringAsync(
                    "https://raw.githubusercontent.com/Trollicus/Shinystrap/main/version.txt");

            if (string.Equals(Version, appVersion.Trim(), StringComparison.OrdinalIgnoreCase))
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
                    await Task.Delay(TimeSpan.FromMinutes(10), token);
                }
                catch (Exception exception)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => { UpdateToggle.IsChecked = false; });

                    SnackbarHelper.ShowError("Shinystrap - Error", $"{exception.Message}");

                    break;
                }
            }
        }

        private void UpdateToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _updateCts.Cancel();
        }

        private void Settings_OnLoaded(object sender, RoutedEventArgs e)
        {
            CurrentVersion.Text = Version;
            ExpFeaturesToggle.IsChecked = Properties.Settings.Default.ExpFeatures;

            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Shiny");
            InstallationPath.Text = folder;
        }

        private void ExpFeatures_Checked(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.HiddenFeatures.Visibility = Visibility.Visible;
            }

            Properties.Settings.Default.ExpFeatures = true;
            _ = Task.Run(() => Properties.Settings.Default.Save());
        }

        private void ExpFeatures_OnUnchecked(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.HiddenFeatures.Visibility = Visibility.Hidden;
            }

            Properties.Settings.Default.ExpFeatures = false;
            _ = Task.Run(() => Properties.Settings.Default.Save());
        }

        private readonly RobloxApi _api = new();

        private async void InstallRoblox_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var installationPath = InstallationPath.Text;
                
                if (!Path.GetFileName(installationPath).Equals("Shiny", StringComparison.OrdinalIgnoreCase))
                {
                    installationPath = Path.Combine(installationPath, "Shiny");
                }

                Directory.CreateDirectory(installationPath);
                
                var drive = new DriveInfo(Path.GetPathRoot(installationPath)!);

                const long requiredSpace = 1L * 1024 * 1024 * 1024;
                if (drive.AvailableFreeSpace < requiredSpace)
                {
                    SnackbarHelper.ShowWarning("Warning", "Not enough disk space!");
                    return;
                }
                
                var currentVersion = await _api.GetRobloxVersionAsync();

                var initializer = new Initialization();

                await initializer.InitializeAsync(
                    currentVersion,
                    installationPath
                );
                
                Properties.Settings.Default.DefaultInstalledPath = installationPath;
                _ = Task.Run(() => Properties.Settings.Default.Save());

                await initializer.SetRobloxProtocol();
                
                SnackbarHelper.ShowSuccess(
                    "Shinystrap",
                    "Roblox installed successfully!"
                );
            }
            catch (Exception exception)
            {
                SnackbarHelper.ShowError(
                    "Shinystrap - Error",
                    exception.Message
                );
            }
        }
        
        //TODO: add roblox auto-update and maybe button to delete, to revert rbx protocol too.
    }
}