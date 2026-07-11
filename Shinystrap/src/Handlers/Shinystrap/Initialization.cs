using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Win32;
using Shinystrap.Handlers.Web;

namespace Shinystrap.Handlers.Shinystrap;

public class Initialization
{
    private readonly HttpHandler _handler = new();

    private const string AppSettings =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Settings>\n\t<ContentFolder>content</ContentFolder>\n\t<BaseUrl>http://www.roblox.com</BaseUrl>\n</Settings>\n";


    private IReadOnlyDictionary<string, string> GetPackageHeaders { get; } = new Dictionary<string, string>
    {
        { "RobloxApp.zip", "" },

        { "ssl.zip", "ssl\\" },
        { "shaders.zip", "shaders\\" },

        { "WebView2.zip", "" },
        { "WebView2RuntimeInstaller.zip", "WebView2RuntimeInstaller\\" },

        { "content-avatar.zip", "content\\avatar\\" },
        { "content-configs.zip", "content\\configs\\" },
        { "content-fonts.zip", "content\\fonts\\" },
        { "content-sky.zip", "content\\sky\\" },
        { "content-sounds.zip", "content\\sounds\\" },
        { "content-textures2.zip", "content\\textures\\" },
        { "content-models.zip", "content\\models\\" },

        { "content-textures3.zip", "PlatformContent\\pc\\textures\\" },
        { "content-terrain.zip", "PlatformContent\\pc\\terrain\\" },
        { "content-platform-fonts.zip", "PlatformContent\\pc\\fonts\\" },
        { "content-platform-dictionaries.zip", "PlatformContent\\pc\\shared_compression_dictionaries\\" },

        { "extracontent-luapackages.zip", "ExtraContent\\LuaPackages\\" },
        { "extracontent-translations.zip", "ExtraContent\\translations\\" },
        { "extracontent-models.zip", "ExtraContent\\models\\" },
        { "extracontent-textures.zip", "ExtraContent\\textures\\" },
        { "extracontent-places.zip", "ExtraContent\\places\\" },
    };

    private List<RobloxPackage> ParsePackageManifest(string manifestText)
    {
        var packages = new List<RobloxPackage>();

        var lines = manifestText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!manifestText.Contains("v0"))
        {
            SnackbarHelper.ShowError("Error", "Unsupported manifest version!");
            return packages;
        }

        int start = lines.Length > 0 && lines[0] == "v0" ? 1 : 0;

        for (int i = start; i + 3 < lines.Length; i += 4)
        {
            try
            {
                packages.Add(new RobloxPackage
                {
                    Name = lines[i],
                    Signature = lines[i + 1],
                    PackedSize = long.Parse(lines[i + 2]),
                    Size = long.Parse(lines[i + 3])
                });

                Console.WriteLine($"{lines[i]} | {lines[i + 1]}");
            }
            catch
            {
                // ignored
            }
        }

        return packages;
    }

    private async Task<string?> DownloadPackages(RobloxPackage package, string downloadsPath, string version)
    {
        var url = $"https://setup.rbxcdn.com/{version}-{package.Name}";
        string downloadPath = Path.Combine(
            downloadsPath,
            $"{package.Signature}-{package.Name}");

        try
        {
            if (File.Exists(downloadPath))
            {
                string hash = await ComputeMd5Async(downloadPath);

                if (hash.Equals(package.Signature, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[Cache Hit] {package.Name}");
                    return downloadPath;
                }

                File.Delete(downloadPath);
            }

            Console.WriteLine($"Downloading: {package.Name}");

            await _handler.DownloadFileAsync(url, downloadPath);

            string downloadedHash = await ComputeMd5Async(downloadPath);

            if (!downloadedHash.Equals(package.Signature, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(downloadPath);
                Console.WriteLine($"MD5 failed for {package.Name}");
                return null;
            }

            Console.WriteLine($"Downloaded: {package.Name}");

            return downloadPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Download failed {package.Name}: {ex.Message}");
            return null;
        }
    }

    private async Task ExtractPackages(RobloxPackage package, string zipPath, string newVersionPath, string version)
    {
        if (!GetPackageHeaders.TryGetValue(package.Name, out var relativePath))
        {
            SnackbarHelper.ShowError("Error - Show this to dev", $"Unknown package: {package.Name}, Version: {version}");
            return;
        }
        
        string extractPath = Path.Combine(newVersionPath, relativePath);

        Directory.CreateDirectory(extractPath);

        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(zipPath);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                string filePath = Path.Combine(extractPath, entry.FullName);

                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                entry.ExtractToFile(filePath, overwrite: true);

                Console.WriteLine($"Extracted: {filePath}");
            }
        });
    }

    private async Task<string> ComputeMd5Async(string filePath)
    {
        using var md5 = MD5.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await md5.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
    
    public async Task InitializeAsync(string version, string installationPath)
    {
        string versionsPath = Path.Combine(installationPath, "Versions");

        Directory.CreateDirectory(versionsPath);

        string newVersionPath = Path.Combine(versionsPath, version);

        if (Directory.Exists(newVersionPath))
            Directory.Delete(newVersionPath, true);

        Directory.CreateDirectory(newVersionPath);

        string downloadsPath = Path.Combine(installationPath, "Downloads");

        Directory.CreateDirectory(downloadsPath);

        var request = await _handler.SendAsync(
            $"https://setup.rbxcdn.com/{version}-rbxPkgManifest.txt",
            HttpMethod.Get);

        string manifest = await request.Content.ReadAsStringAsync();

        var packages = ParsePackageManifest(manifest);

        var packagePaths = new Dictionary<RobloxPackage, string>();

        // Download packages
        foreach (var package in packages)
        {
            var path = await DownloadPackages(package, downloadsPath, version);

            if (path != null)
                packagePaths.Add(package, path);
        }

        var extractionTasks = new List<Task>();

        // Extract zip packages
        foreach (var package in packagePaths)
        {
            if (!package.Key.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                continue;

            if (package.Key.Name == "WebView2RuntimeInstaller.zip")
                continue;

            extractionTasks.Add(
                ExtractPackages(
                    package.Key,
                    package.Value,
                    newVersionPath,
                    version));
        }

        await Task.WhenAll(extractionTasks);

        await File.WriteAllTextAsync(
            Path.Combine(newVersionPath, "AppSettings.xml"),
            AppSettings);
    }

    public Task SetRobloxProtocol()
    {
        return Task.Run(() =>
            {
                var currentProcess =
                    Environment.ProcessPath ??
                    Path.Combine(AppContext.BaseDirectory, "Shinystrap.exe");

                var value = $"\"{currentProcess}\" \"%1\"";

                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\roblox-player\shell\open\command",
                    writable: true);

                if (key is null)
                    throw new Exception("Registry key not found.");

                key.SetValue("", value);
            })
            .ContinueWith(_ =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SnackbarHelper.ShowSuccess("Shinystrap", "Initialized!");
                });
            });
    }
}