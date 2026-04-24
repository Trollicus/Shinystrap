using System.Net;
using System.Security.Principal;
using System.Windows;
using Shinystrap.Handlers.Roblox;
using Shinystrap.Handlers.Shinystrap;
using Shinystrap.Pages;
using Wpf.Ui;

namespace Shinystrap;

public partial class MainWindow
{
    private readonly RobloxApi _robloxApi = new();
    
    public MainWindow()
    {
        InitializeComponent();
        var snackbarService = new SnackbarService();
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        SnackbarHelper.Initialize(snackbarService);
    }

    private async void FluentWindow_Loaded(object sender, RoutedEventArgs e)
    {
        MainNavigation.Navigate(typeof(Addons));

        if (!IsAdministrator())
        {
            SnackbarHelper.ShowError("Shinystrap", "Please run the application as administrator!");
            await Task.Delay(TimeSpan.FromMinutes(1));
            Environment.Exit(0);
        }
        
        _ = ChecksAsync();
    }
    
    bool IsAdministrator()
    {
        #pragma warning disable CA1416
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
    
    private async Task ChecksAsync()
    {
        while (true)
        {
            if (!HasInternet())
            {
                SnackbarHelper.ShowError("Internet", "Internet unavailable, please connect to the internet and try again!");
            }
        
            if (await _robloxApi.CheckForUpdatesAsync())
            {
                SnackbarHelper.ShowWarning("Roblox", "Version mismatch! Please update your Roblox", TimeSpan.FromSeconds(5));
            }

            await Task.Delay(TimeSpan.FromMinutes(10));
        }
    }
    
    bool HasInternet()
    {
        try
        {
            var entry = Dns.GetHostEntry("dns.google");
            return entry.AddressList.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private void FluentWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        Environment.Exit(0);
        //e.Cancel = true;
        //Hide();
    }

    private void TrayQuit_Click(object sender, RoutedEventArgs e)
    {
        Environment.Exit(0);
    }
}