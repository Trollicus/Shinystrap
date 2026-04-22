using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shinystrap.Handlers.Roblox;

namespace Shinystrap.Pages
{
    public partial class GameHistory : Page
    {
        public GameHistory()
        {
            InitializeComponent();
            HistoryPanel.ItemsSource = RobloxManager.GameHistory;
        }
        
        
        public class GameHistoryItem
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public string PlaceId { get; set; }      
            public ImageSource ImageSource { get; set; }
        }

        private void JoinGame_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            if (button == null) return;
            
            var gameHistory = button.GetValue(Wpf.Ui.Controls.Button.CommandParameterProperty) as GameHistoryItem 
                              ?? button.DataContext as GameHistoryItem;

            if (gameHistory == null) return;
            
            //TODO: Find way to get rbx-auth-ticket without cookie and perhaps launch the game?
            Process.Start($"https://www.roblox.com/games/{gameHistory.PlaceId}");
        }
    }
}
