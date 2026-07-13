using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using PuppeteerSharp;
using Shinystrap.Handlers.Roblox;
using Shinystrap.Handlers.Shinystrap;
using Shinystrap.Handlers.Web;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using Page = System.Windows.Controls.Page;

namespace Shinystrap.Pages;

public partial class AccountManager : Page
{
    public class Account : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string ImageSource { get; set; } = "";
        public required string AccountBiscuit { get; set; } = "";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
    
    private record AccountRecord(string Title, string Description, string ImageSource, string EncryptedBiscuit, bool IsSelected);
    public ObservableCollection<Account> Accounts { get; set; } = [];
    
    private readonly string _storePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Shinystrap", "accounts.json");
    
    private byte[] EncryptBiscuit(string biscuit) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(biscuit), null, DataProtectionScope.CurrentUser);

    private string DecryptBiscuit(byte[] encrypted) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
    
    private void SaveAccounts()
    {
        try
        {
            var dir = Path.GetDirectoryName(_storePath)!;
            Directory.CreateDirectory(dir);

            var records = Accounts.Select(a => new AccountRecord(
                a.Title,
                a.Description,
                a.ImageSource,
                Convert.ToBase64String(EncryptBiscuit(a.AccountBiscuit)),
                a.IsSelected
            )).ToList();

            File.WriteAllText(_storePath, JsonSerializer.Serialize(records));
        }
        catch (Exception exception)
        {
            SnackbarHelper.ShowError("Error", "Failed to save accounts.");
            MessageBox.Show("Error", $"Show this to mod/owner: \n{exception.Message}");
        }
    }
    
    private void LoadAccounts()
    {
        try
        {
            if (!File.Exists(_storePath))
                return;

            var json = File.ReadAllText(_storePath);
            var records = JsonSerializer.Deserialize<List<AccountRecord>>(json);
            if (records is null)
                return;

            Account? selected = null;

            foreach (var record in records)
            {
                string biscuit;
                try
                {
                    biscuit = DecryptBiscuit(Convert.FromBase64String(record.EncryptedBiscuit));
                }
                catch
                {
                    continue;
                }

                var account = new Account
                {
                    Title = record.Title,
                    Description = record.Description,
                    ImageSource = record.ImageSource,
                    AccountBiscuit = biscuit,
                    IsSelected = record.IsSelected
                };

                Accounts.Add(account);

                if (account.IsSelected)
                    selected = account;
            }

            if (selected != null)
                RobloxManager.RobloxBiscuit = selected.AccountBiscuit;
        }
        catch (Exception exception)
        {
            SnackbarHelper.ShowError("Error", "Failed to load saved accounts.");
            MessageBox.Show("Error", $"Show this to mod/owner: \n{exception.Message}");
        }
    }

    
    public AccountManager()
    {
        InitializeComponent();
        DataContext = this;
        
        LoadAccounts();
    }
    
    private void AccountSwitch_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { DataContext: Account selected })
            return;

        foreach (var account in Accounts)
            account.IsSelected = ReferenceEquals(account, selected);
        
        RobloxManager.RobloxBiscuit = selected.AccountBiscuit;
        
        SaveAccounts();
    }
    
    private void AccountSwitch_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { DataContext: Account account })
            return;

        account.IsSelected = false;
        RobloxManager.RobloxBiscuit = "";
        
        SaveAccounts();
    }
    
    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (Accounts.FirstOrDefault(a => a.IsSelected) is not Account selected)
            return;

        Accounts.Remove(selected);
        RobloxManager.RobloxBiscuit = "";
        
        SaveAccounts();
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        string biscuit = "";
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
                    biscuit = authCookie.Value;
                    found = true;
                }
                else
                {
                    await Task.Delay(1000);
                }
                    
            } while (!found);

            if (found)
            {
                var (name, id) = await GetAccountInformationAsync(biscuit);
                var avatar = await GetAccountAvatar(id);
                
                foreach (var account in Accounts)
                    account.IsSelected = false;
                
                var newAccount = new Account
                {
                    AccountBiscuit = biscuit,
                    Title = name,
                    Description = $"ID: {id}",
                    ImageSource = avatar ?? "",
                    IsSelected = true
                };

                Accounts.Add(newAccount);
                RobloxManager.RobloxBiscuit = newAccount.AccountBiscuit;
                SaveAccounts();
                
                SnackbarHelper.ShowSuccess("Success", "Successfully added account.");
                await browser.CloseAsync();
            }
            else
            {
                SnackbarHelper.ShowError("Error", "Something went wrong.");
            }
        }
        catch(Exception exception)
        {
            SnackbarHelper.ShowError("Error", "Something went wrong.");
            MessageBox.Show("Error", $"Show this to mod/owner: \n{exception.Message}");
           
        }
    }
    private readonly HttpHandler _handler = new HttpHandler();

    private record AuthenticatedUserResponse(long Id, string Name);
    private record AvatarThumbnailResponse(List<AvatarThumbnailData> Data);
    private record AvatarThumbnailData(string ImageUrl);

    private async Task<(string Name, long Id)> GetAccountInformationAsync(string cookie)
    {
        var request = await _handler.SendAsync("https://users.roblox.com/v1/users/authenticated", HttpMethod.Get, new []
        {
            new HttpHandler.RequestHeadersEx("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0"),
            new HttpHandler.RequestHeadersEx("Cookie", $".ROBLOSECURITY={cookie}")
        });

        var user = await request.Content.ReadFromJsonAsync<AuthenticatedUserResponse>();
        return (user!.Name, user.Id);
    }

    private async Task<string?> GetAccountAvatar(long id)//maybe use robloxapi's getuserthumbnail instead of ts
    {
        var request = await _handler.SendAsync(
            $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={id}&size=150x150&format=Png&isCircular=true",
            HttpMethod.Get);

        var thumb = await request.Content.ReadFromJsonAsync<AvatarThumbnailResponse>();
        return thumb?.Data.FirstOrDefault()?.ImageUrl;
    }
}