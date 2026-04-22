using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Shinystrap.Handlers.Shinystrap;

public class SnackbarHelper
{
    private static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(3);
 
    private static ISnackbarService? _service;
 
    public static void Initialize(ISnackbarService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));
 
    public static void ShowSuccess(string title, string message, TimeSpan? timeout = null)
        => Show(title, message, ControlAppearance.Success,
            new SymbolIcon(SymbolRegular.CheckmarkCircle24), timeout);
 
    public static void ShowError(string title, string message, TimeSpan? timeout = null)
        => Show(title, message, ControlAppearance.Danger,
            new SymbolIcon(SymbolRegular.DismissCircle24), timeout);
 
    public static void ShowWarning(string title, string message, TimeSpan? timeout = null)
        => Show(title, message, ControlAppearance.Caution,
            new SymbolIcon(SymbolRegular.Warning24), timeout);
 
    public static void ShowInfo(string title, string message, TimeSpan? timeout = null)
        => Show(title, message, ControlAppearance.Secondary,
            new SymbolIcon(SymbolRegular.Info24), timeout);

    private static void Show(
        string title,
        string message,
        ControlAppearance appearance = ControlAppearance.Primary,
        IconElement? icon = null,
        TimeSpan? timeout = null)
    {
        if (_service is null)
            throw new InvalidOperationException(
                "SnackbarHelper is not initialised. " +
                "Call SnackbarHelper.Initialize(snackbarService) first.");
 
        var dispatcher = System.Windows.Application.Current.Dispatcher;
 
        void DoShow() => _service.Show(title, message, appearance, icon, timeout ?? DefaultTimeout);
 
        if (dispatcher.CheckAccess())
            DoShow();
        else
            dispatcher.Invoke(DoShow);
    }
}