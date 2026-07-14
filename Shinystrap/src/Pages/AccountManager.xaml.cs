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
        DataContext = AccountManaging.Instance;
    }
    
    private void AccountSwitch_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { DataContext: AccountManaging.Account selected })
            return;

        AccountManaging.Instance.SetCurrentAccount(selected);
    }
    
    private void AccountSwitch_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { DataContext: AccountManaging.Account account })
            return;
        
        account.IsSelected = false;
        AccountManaging.Instance.CurrentAccount = null;
        AccountManaging.Instance.SaveAccounts();
    }
    
    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (AccountManaging.Instance.CurrentAccount is { } selected)
        {
            AccountManaging.Instance.RemoveAccount(selected);
        }
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
                await AccountManaging.Instance.AddAccountAsync(biscuit);
                
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
}