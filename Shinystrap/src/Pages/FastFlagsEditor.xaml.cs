using Microsoft.Win32;
using Shinystrap.Handlers.Roblox;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Shinystrap.Handlers.Shinystrap;

namespace Shinystrap.Pages
{
    public partial class FastFlagsEditor
    {
        //https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569

        public class FlagItem
        {
            public string Key { get; set; } = string.Empty;
            public object Value { get; set; } = string.Empty;
        }

        public ObservableCollection<FlagItem> Items { get; set; } = new();

        public FastFlagsEditor()
        {
            InitializeComponent();
            DataContext = this;
            
            SnackbarHelper.ShowInfo("Warning!", "There's Allowlist by Roblox about the flags, se at your own risk!");
        }

        public async Task CreateFFlagsFile()
        {
            var api = new RobloxApi();
            var currentVersion = await api.GetRobloxVersionAsync();
            
            var robloxPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox",
                currentVersion,
                "ClientSettings");
            
            Directory.CreateDirectory(robloxPath);

            var filePath = Path.Combine(robloxPath, "ClientAppSettings.json");

            if (!File.Exists(filePath))
            {
                await using var _ = File.Create(filePath);
            }
        }

        private void AddFlag_Click(object sender, RoutedEventArgs e)
        {
            var newItem = new FlagItem { Key = "Name", Value = "Value" };
            Items.Add(newItem);

            FlagsDataGrid.SelectedItem = newItem;
            FlagsDataGrid.ScrollIntoView(newItem);
            FlagsDataGrid.CurrentCell = new DataGridCellInfo(newItem, FlagsDataGrid.Columns[0]);
            FlagsDataGrid.BeginEdit();
        }

        private void DeleteFlag_Click(object sender, RoutedEventArgs e)
        {
            if (FlagsDataGrid.SelectedItem is FlagItem selected)
                Items.Remove(selected);
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (Items.Count == 0) return;

            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = "ClientAppSettings.json"
            };

            if (dialog.ShowDialog() != true) return;

            var dict = Items.ToDictionary(f => f.Key, f => f.Value);
            var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dialog.FileName, json);
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json"
            };

            if (dialog.ShowDialog() != true) return;

            var json = File.ReadAllText(dialog.FileName);
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            if (dict == null) return;

            Items.Clear();
            foreach (var kvp in dict)
            {
                object value = kvp.Value.ValueKind switch
                {
                    JsonValueKind.Number => kvp.Value.GetUInt32(),
                    _ => kvp.Value.GetString() ?? string.Empty
                };

                Items.Add(new FlagItem { Key = kvp.Key, Value = value });
            }
        }

        public RobloxApi Api = new();

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            await CreateFFlagsFile();
            
            var currentVersion = await Api.GetRobloxVersionAsync();

            var filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox",
                currentVersion,
                "ClientSettings",
                "ClientAppSettings.json");
            
            var dict = Items.ToDictionary(x => x.Key, x => x.Value);

            var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(filePath, json);

            SnackbarHelper.ShowInfo("Success!", "Successfully saved Fast Flags!");
        }

        private async void FastFlagsEditor_OnLoaded(object sender, RoutedEventArgs e)
        {
            var filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox",
                await Api.GetRobloxVersionAsync(),
                "ClientSettings",
                "ClientAppSettings.json");

            if (!File.Exists(filePath)) return;
            var json = await File.ReadAllTextAsync(filePath);

            Console.WriteLine(json);
                
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict == null) return;

            Items.Clear();

            foreach (var kvp in dict)
            {
                Items.Add(new FlagItem
                {
                    Key = kvp.Key,
                    Value = kvp.Value
                });
            }
        }
    }
}
