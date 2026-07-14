using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shinystrap.Handlers.Shinystrap;
using Shinystrap.Handlers.Web;
using Shinystrap.Pages;

namespace Shinystrap.Handlers.Roblox;

public class AccountManaging
{
    public static AccountManaging Instance { get; } = new();

    public ObservableCollection<Account> Accounts { get; } = [];
    
    private Account? _currentAccount;
    public Account? CurrentAccount
    {
        get => _currentAccount;
        set
        {
            _currentAccount = value;
            RobloxManager.RobloxBiscuit = value?.AccountBiscuit ?? "";
            PropertyChanged?.Invoke(this, new(nameof(CurrentAccount)));
        }
    }
    
    private readonly string _storePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Shinystrap", "accounts.json");
    
    public event PropertyChangedEventHandler? PropertyChanged;

    private AccountManaging() { }
    
    private record AccountRecord(string Title, string Description, string ImageSource, 
        string EncryptedBiscuit, bool IsSelected);
    
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
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
    
    private static byte[] EncryptBiscuit(string biscuit) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(biscuit), null, DataProtectionScope.CurrentUser);

    private static string DecryptBiscuit(byte[] encrypted) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));

    public async void LoadAccounts()
    {
        var api = new RobloxApi();
        try
        {
            if (Accounts.Count > 0) return;
            if (!File.Exists(_storePath)) return;

            var json = await File.ReadAllTextAsync(_storePath);
            var records = JsonSerializer.Deserialize<List<AccountRecord>>(json);
            if (records is null) return;

            Accounts.Clear();

            foreach (var record in records)
            {
                try
                {
                    var biscuit = DecryptBiscuit(Convert.FromBase64String(record.EncryptedBiscuit));

                    bool isValid = await api.ValidateCookie(biscuit);

                    if (!isValid)
                    {
                        SnackbarHelper.ShowWarning("Warning", $"Removed expired account: {record.Title}");
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
                        CurrentAccount = account;
                }
                catch
                {
                    // Skip corrupted entries
                }
            }
            
            SaveAccounts();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed loading Account: {ex.Message}");
        }
    }
    
    public void SaveAccounts()
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
        catch (Exception ex)
        {
            SnackbarHelper.ShowError("Error", "Failed to save accounts. " + ex.Message);
        }
    }
    
    public void SetCurrentAccount(Account account)
    {
        foreach (var acc in Accounts)
            acc.IsSelected = ReferenceEquals(acc, account);

        CurrentAccount = account;
        SaveAccounts();
    }

    public void RemoveAccount(Account account)
    {
        if (CurrentAccount == account)
            CurrentAccount = null;

        Accounts.Remove(account);
        SaveAccounts();
    }

    public async Task AddAccountAsync(string biscuit)
    {
        if (string.IsNullOrWhiteSpace(biscuit))
            return;
        
        var (name, id) = await GetAccountInformationAsync(biscuit);
        var avatar = await GetAccountAvatar(id);
        
        foreach (var acc in Accounts)
            acc.IsSelected = false;

        var newAccount = new Account
        {
            AccountBiscuit = biscuit,
            Title = name,
            Description = $"ID: {id}",
            ImageSource = avatar ?? "",
            IsSelected = true
        };

        Accounts.Add(newAccount);
        CurrentAccount = newAccount;
        SaveAccounts();

        SnackbarHelper.ShowSuccess("Success", $"Added account: {name}");
    }
    
    private readonly HttpHandler _handler = new HttpHandler();

    private record AuthenticatedUserResponse(long Id, string Name);
    private record AvatarThumbnailResponse(List<AvatarThumbnailData> Data);
    private record AvatarThumbnailData(string ImageUrl);

    private async Task<(string Name, long Id)> GetAccountInformationAsync(string cookie)
    {
        var request = await _handler.SendAsync("https://users.roblox.com/v1/users/authenticated", HttpMethod.Get, [
            new HttpHandler.RequestHeadersEx("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0"),
            new HttpHandler.RequestHeadersEx("Cookie", $".ROBLOSECURITY={cookie}")
        ]);

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