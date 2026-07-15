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
        public Settings()
        {
            InitializeComponent();
        }
        
        private async void CheckUpdate_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await ((App)Application.Current).CheckForUpdatesAsync();
            }
            catch (Exception exception)
            {
                SnackbarHelper.ShowError("Shinystrap - Error", $"{exception.Message}");
            }
        }
        
        
        private void UpdateToggle_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.ShinyAutoUpdate = true;
            _ = Task.Run(() => Properties.Settings.Default.Save());

            ((App)Application.Current).StartAppAutoUpdate();
        }
        

        private void UpdateToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.ShinyAutoUpdate = false;
            _ = Task.Run(() => Properties.Settings.Default.Save());

            ((App)Application.Current).StopAppAutoUpdate();
        }
        
        private void Settings_OnLoaded(object sender, RoutedEventArgs e)
        {
            CurrentVersion.Text = App.Version;
            ExpFeaturesToggle.IsChecked = Properties.Settings.Default.ExpFeatures;
            RbxAutoUpdateToggle.IsChecked = Properties.Settings.Default.RbxAutoUpdate;
            
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
        
        private void RbxAutoUpdateToggle_OnChecked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.RbxAutoUpdate = true;
            _ = Task.Run(() => Properties.Settings.Default.Save());

            ((App)Application.Current).StartRbxAutoUpdate();
        }
        
        private void RbxAutoUpdateToggle_OnUnchecked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.RbxAutoUpdate = false;
            _ = Task.Run(() => Properties.Settings.Default.Save());
            

            ((App)Application.Current).StopRbxAutoUpdate();
        }

        private void BrowsePath_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();

            if (dialog.ShowDialog() == true)
            {
                InstallationPath.Text = dialog.FolderName;
            }
        }

        private void UnInstallRbx_OnClick(object sender, RoutedEventArgs e)
        {
            var defaultPath = Properties.Settings.Default.DefaultInstalledPath;

            if (Directory.Exists(defaultPath))
            {
                try
                {
                    Directory.Delete(defaultPath, true);
                }
                catch (IOException ex)
                {
                    SnackbarHelper.ShowError("Error", $"Failed to remove installation folder: {ex.Message}");
                    return;
                }
            }
            
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var versionsPath = Path.Combine(basePath, "Roblox", "Versions");
            
            var latestVersion = new DirectoryInfo(versionsPath)
                .GetDirectories()
                .OrderByDescending(d => d.LastWriteTime)
                .FirstOrDefault();

            if (latestVersion == null) return;
            string versionName = latestVersion.Name;
               
            var robloxExe = Path.Combine(
                versionsPath,
                versionName,
                "RobloxPlayerBeta.exe"
            );
                
            if (!File.Exists(robloxExe))
            {
                SnackbarHelper.ShowError("Error", "Roblox executable not found, please reinstall roblox manually!");
                return;
            }
                
            var value = $"\"{robloxExe}\" \"%1\"";

            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\roblox-player\shell\open\command",
                writable: true);

            if (key is null)
            {
                SnackbarHelper.ShowError("Error", "Registry key not found, please reinstall roblox manually!");
                return;
            }

            key.SetValue("", value);
                
            SnackbarHelper.ShowSuccess("Success","Successfully deleted Roblox installed by Shinystrap!");
        }
    }
}