using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shinystrap.Handlers.Roblox;

public static class AccountHandler
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Shinystrap",
        "accounts.json");


    private record AccountRecord(
        string Title,
        string Description,
        string ImageSource,
        string EncryptedBiscuit,
        bool IsSelected);


    public static void LoadAccounts()
    {
        try
        {
            if (!File.Exists(StorePath))
                return;

            var json = File.ReadAllText(StorePath);

            var records = JsonSerializer.Deserialize<List<AccountRecord>>(json);

            if (records == null)
                return;


            foreach (var record in records)
            {
                if (!record.IsSelected)
                    continue;

                var biscuit = Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(
                        Convert.FromBase64String(record.EncryptedBiscuit),
                        null,
                        DataProtectionScope.CurrentUser));

                RobloxManager.RobloxBiscuit = biscuit;
                break;
            }
        }
        catch
        {
            // ignore loading errors
        }
    }
}