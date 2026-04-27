using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using PuppeteerSharp;
using Shinystrap.Handlers.Roblox;
using Shinystrap.Handlers.Shinystrap;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
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
            public required ImageSource ImageSource { get; init; }
        }
        public GameHistory()
        {
            InitializeComponent();
            HistoryPanel.ItemsSource = RobloxManager.GameHistory;
            LogoutBtn.IsEnabled = false;
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

            if (_robloSecurity is null)
            {
                SnackbarHelper.ShowWarning("Warning", "please login first!");
                return;
            }
            
            var api = new RobloxApi();
            var currentVersion = await api.GetRobloxVersionAsync();
            
            if (await api.CheckForUpdatesAsync())
            {
                SnackbarHelper.ShowWarning("Roblox", "Version mismatch! Please update your Roblox", TimeSpan.FromSeconds(5));
            }
            
            var robloxPath =
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");
            
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(robloxPath +
                                        $@"\Versions\{currentVersion}\RobloxPlayerBeta.exe"),
                Arguments =
                    $"--app -t {await api.GetAuthenticationTicketAsync(_robloSecurity)} -j https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={DateNow()}&placeId={gameHistory.PlaceId} -LaunchExp InApp"
            });
        }
        
        long DateNow()
        {
            return ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();
        }

        private string? _robloSecurity;

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            LoginBtn.IsEnabled = false;

            try
            {
                var fetcher = new BrowserFetcher();
                await fetcher.DownloadAsync();
                
                await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = false,
                    DefaultViewport = null
                });
                
                var page = await browser.NewPageAsync();
                await page.GoToAsync("https://roblox.com/login");
                
                var found = false;

                do
                {
                    if (browser.IsClosed) break;
                    
                    var cookies = await page.GetCookiesAsync();
                    var authCookie = cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY");
                    
                    if (authCookie != null && !string.IsNullOrEmpty(authCookie.Value))
                    {
                        _robloSecurity = authCookie.Value;
                        found = true;
                    }
                    else
                    {
                        await Task.Delay(1000);
                    }
                    
                } while (!found);

                if (found)
                {
                    RobloxManager.RobloxBiscuit = _robloSecurity;
                    SnackbarHelper.ShowSuccess("Logged In", "Successfully logged in.");
                    LogoutBtn.IsEnabled = true;
                    await browser.CloseAsync();
                }
                else
                {
                    LoginBtn.IsEnabled = true;
                }
            }
            catch(Exception exception)
            {
                SnackbarHelper.ShowError("Login Error", "Something went wrong.");
                MessageBox.Show("Error", $"Show this to mod/owner: \n{exception.Message}");
                LoginBtn.IsEnabled = true;
            }
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            _robloSecurity = string.Empty;
            LogoutBtn.IsEnabled = false;
            LoginBtn.IsEnabled = true;
            
            RobloxManager.RobloxBiscuit = string.Empty;
            
            SnackbarHelper.ShowSuccess("Logged Out", "Successfully logged out.");
        }

        private void GameHistory_OnLoaded(object sender, RoutedEventArgs e)
        {
            var biscuit = RobloxManager.RobloxBiscuit;
            
            if (!string.IsNullOrEmpty(biscuit))
            {
                LoginBtn.IsEnabled = false;
                LogoutBtn.IsEnabled = true;
            }
            else
            {
                LoginBtn.IsEnabled = true;
                LogoutBtn.IsEnabled = false;
            }
        }
    }
}
