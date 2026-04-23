using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shinystrap.Handlers.Roblox;

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
        }
        
        private void JoinGame_OnClick(object sender, RoutedEventArgs e)
        {
            //TODO: Find way to get rbx-auth-ticket without cookie and perhaps launch the game?
            if (sender is not FrameworkElement element)
            {
                return;
            }

            var gameHistory =
                element.GetValue(Wpf.Ui.Controls.Button.CommandParameterProperty) as GameHistoryItem ??
                element.DataContext as GameHistoryItem;

            if (gameHistory is null)
            {
                return;
            }

            var url = $"https://www.roblox.com/games/{gameHistory.PlaceId}";

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
    }
}
