using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Shinystrap.Handlers.Roblox;
using Shinystrap.Handlers.Shinystrap;
using Wpf.Ui.Controls;
using Page = System.Windows.Controls.Page;

namespace Shinystrap.Pages
{
    public partial class GameHistory : Page
    {
        public sealed class GameHistoryItem
        {
            public required string Title { get; init; }
            public required string Description { get; init; }
            public required string PlaceId { get; init; }
            public required object ImageSource { get; init; }
        }
        public GameHistory()
        {
            InitializeComponent();
            HistoryPanel.ItemsSource = RobloxManager.GameHistory;
        }
        
        private async void JoinGame_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            var gameHistory =
                element.GetValue(Button.CommandParameterProperty) as GameHistoryItem ??
                element.DataContext as GameHistoryItem;

            if (gameHistory is null)
            {
                return;
            }

            var robloSecurity = RobloxManager.RobloxBiscuit;
            
            if (string.IsNullOrEmpty(robloSecurity))
            {
                SnackbarHelper.ShowWarning("Warning", "Please add account in Account Manager!");
                return;
            }
            var api = new RobloxApi();
            
            bool isValid = await api.ValidateCookie(robloSecurity);

            if (!isValid)
            {
                SnackbarHelper.ShowWarning("Warning", $"Invalid/Expired Account!");
                return;
            }
            
            var currentVersion = await api.GetRobloxVersionAsync();
            
            if (await api.CheckForUpdatesAsync())
            {
                SnackbarHelper.ShowWarning("Roblox", "Version mismatch! Please update your Roblox", TimeSpan.FromSeconds(5));
                return;
            }
            
            var robloxExe = Path.Combine(
                Properties.Settings.Default.DefaultInstalledPath,
                "Versions",
                currentVersion,
                "RobloxPlayerBeta.exe");

            if (!File.Exists(robloxExe))
            {
                SnackbarHelper.ShowError("Error", "Please initialize Shinystrap first before using this feature!");
            }
            
            Process.Start(new ProcessStartInfo
            {
                FileName = robloxExe,
                Arguments =
                    $"--app -t {await api.GetAuthenticationTicketAsync(robloSecurity)} -j https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={DateNow()}&placeId={gameHistory.PlaceId} -LaunchExp InApp"
            });
        }
        
        long DateNow()
        {
            return ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();
        }
        
    }
}
