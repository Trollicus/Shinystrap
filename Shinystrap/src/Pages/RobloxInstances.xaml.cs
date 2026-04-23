using System.Diagnostics;
using System.Windows;
using Shinystrap.Handlers.Roblox;

namespace Shinystrap.Pages;

public partial class RobloxInstances
{
    public sealed record RobloxInstance
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required object ImageSource { get; init; }
        public int ProcessId { get; init; }
    }

    public RobloxInstances()
    {
        InitializeComponent();
        InstancesLists.ItemsSource = RobloxManager.ActiveInstances;
    }

    private void Disconnect_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        var instance =
            element.GetValue(Wpf.Ui.Controls.Button.CommandParameterProperty) as RobloxInstance ??
            element.DataContext as RobloxInstance;

        if (instance is null)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(instance.ProcessId);
            process.Kill();

            RobloxManager.ActiveInstances.Remove(instance);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error disconnecting Roblox process {instance.ProcessId}: {ex}");

            // If the process is already gone, removing it from the UI is reasonable.
            RobloxManager.ActiveInstances.Remove(instance);
        }
    }
}