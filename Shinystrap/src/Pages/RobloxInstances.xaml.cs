using System.Diagnostics;
using System.Windows;
using Shinystrap.Handlers.Roblox;

namespace Shinystrap.Pages
{
    public partial class RobloxInstances
    {
        public class RobloxInstance
        {
            public required string Title { get; set; }
            public required string Description { get; set; }
            public required object ImageSource { get; set; }
            public int ProcessId { get; init; }
        }
        
        public RobloxInstances()
        {
            InitializeComponent();
            InstancesLists.ItemsSource = RobloxManager.ActiveInstances;
        }
        
        private void Disconnect_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            if (button == null) return;
            
            var instance = button.GetValue(Wpf.Ui.Controls.Button.CommandParameterProperty) as RobloxInstance 
                           ?? button.DataContext as RobloxInstance;

            if (instance == null) return;

            try
            {
                Process proc = Process.GetProcessById(instance.ProcessId);
                proc.Kill();
                
                RobloxManager.ActiveInstances.Remove(instance);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disconnecting: {ex.Message}");
                RobloxManager.ActiveInstances.Remove(instance);
            }
        }
    }
}