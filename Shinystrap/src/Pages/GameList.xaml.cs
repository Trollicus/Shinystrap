using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using Shinystrap.Handlers.Roblox;
using Shinystrap.Handlers.Shinystrap;
using Shinystrap.Handlers.Web;

namespace Shinystrap.Pages
{
    /// <summary>
    /// Interaction logic for GameList.xaml
    /// </summary>
    public partial class GameList : Page
    {
        public class GameCard
        {
            public required string Title { get; set; }
            public required string Description { get; set; }
            public required string ImageUrl { get; set; }
            public string? PlaceId { get; set; }
        }
        
        private readonly HttpHandler _httpHandler = new();
        private List<GameCard> _allCards = new();
        
        public GameList()
        {
            InitializeComponent();
        }
        
        private async void GameList_OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var cards = await GetGameCardsAsync();
                _allCards = cards;
                GameCards.ItemsSource = _allCards;
            }
            catch (Exception exception)
            {
                SnackbarHelper.ShowError("Failed to load game cards", exception.Message);
            }
        }
        
        
        private async void JoinButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button button)
            {
                if (string.IsNullOrEmpty(RobloxManager.RobloxBiscuit))
                {
                    SnackbarHelper.ShowWarning("Warning", "Please login first in GameHistory before using the Join!");
                    return;
                }
                
                var placeId = button.Tag?.ToString();
                Console.WriteLine($"Place Id: {placeId}");
                
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
                        $"--app -t {await api.GetAuthenticationTicketAsync(RobloxManager.RobloxBiscuit)} -j https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={DateNow()}&placeId={placeId} -LaunchExp InApp"
                });
            }
        }
        
        long DateNow()
        {
            return ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();
        }

        private void SearchGame_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox box)
                return;

            var query = box.Text?.Trim().ToLower();

            if (string.IsNullOrWhiteSpace(query))
            {
                GameCards.ItemsSource = _allCards;
                return;
            }

            GameCards.ItemsSource = _allCards
                .Where(x => x.Title != null && x.Title.ToLower().Contains(query))
                .ToList();
        }

        private async Task<List<GameCard>> GetGameCardsAsync()
        {
            Console.WriteLine("asd");
            var request = await _httpHandler.SendAsync("https://apis.roblox.com/explore-api/v1/get-sorts?sessionId=ac43cb8c-ee7c-49da-8cef-dcfd9287f07b&cpuCores=16&maxResolution=2432x1520", HttpMethod.Get);
            
            var response = await request.Content.ReadAsStringAsync();
            
            var gamesResponse = JsonSerializer.Deserialize<GamesResponse>(response);

            if (gamesResponse?.Sorts == null)
                return new List<GameCard>();

            const int maxGames = 10;
            
            var games = gamesResponse.Sorts
                .Where(s => s?.Games != null)
                .SelectMany(s => s.Games)
                .Take(maxGames)
                .ToList();

            var tasks = games.Select(async game =>
            {
                var imageUrl = await GetGameThumbnail(game.UniverseId);

                return new GameCard
                {
                    Title = game.GameName,
                    Description = "Roblox experience",
                    ImageUrl = imageUrl,
                    PlaceId = game.RootPlaceId.ToString()
                };
            });

            return (await Task.WhenAll(tasks)).ToList();
        }

        private async Task<string> GetGameThumbnail(object universeId)
        {
            var request = await _httpHandler.SendAsync(
                $"https://thumbnails.roblox.com/v1/games/multiget/thumbnails?universeIds={universeId}&size=768x432&format=Png&isCircular=false",
                HttpMethod.Get);

            var response = await request.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.GetArrayLength() == 0)
                return "https://upload.wikimedia.org/wikipedia/commons/a/a3/Image-not-found.png";

            var first = data[0];

            if (!first.TryGetProperty("thumbnails", out var thumbs) ||
                thumbs.GetArrayLength() == 0)
                return "https://upload.wikimedia.org/wikipedia/commons/a/a3/Image-not-found.png";

            var imageUrl = thumbs[0].GetProperty("imageUrl").GetString();

            return imageUrl ?? "https://upload.wikimedia.org/wikipedia/commons/a/a3/Image-not-found.png";
        }

        public class GamesResponse
        {
            [JsonPropertyName("sorts")] public required List<Sort> Sorts { get; init; }
            
            public class Sort
            {
                [JsonPropertyName("games")] public List<Game> Games { get; set; }
            }

            public class Game
            {
                [JsonPropertyName("universeId")] public required object UniverseId { get; set; }

                [JsonPropertyName("rootPlaceId")] public required object RootPlaceId { get; set; }
                
                [JsonPropertyName("name")] public required string GameName { get; set; }
            }
        }
    }
}
