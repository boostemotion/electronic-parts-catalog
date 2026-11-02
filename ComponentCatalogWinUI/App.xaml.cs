using Microsoft.UI.Xaml;
using ComponentCatalogWinUI.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ComponentCatalogWinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    public Window? MainWindow => _window;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();

        if (!SingleInstanceGuard.TryAcquire())
        {
            AppLogger.WriteStartup("SingleInstance.SecondaryExit");
            SingleInstanceGuard.ActivateExistingInstance();
            Environment.Exit(0);
            return;
        }

        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += AppDomain_UnhandledException;
        AppDomain.CurrentDomain.FirstChanceException += AppDomain_FirstChanceException;
        AppLogger.WriteStartup("App ctor");
    }

    private static int _firstChanceExceptionCount;

    /// <summary>
    /// XAML re-throws managed exceptions through COM, which terminates the process before the
    /// UnhandledException event can run. First-chance logging captures the original exception
    /// together with its stack trace, which is the only way to diagnose those crashes.
    /// </summary>
    private static void AppDomain_FirstChanceException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        if (Interlocked.Increment(ref _firstChanceExceptionCount) > 200)
        {
            return;
        }

        var exception = e.Exception;
        if (exception is System.Threading.Tasks.TaskCanceledException or OperationCanceledException)
        {
            return;
        }

        // Expected on machines where HttpListener cannot initialise; already reported via
        // Api.Startup.Error. Filtering keeps the log readable without hiding real failures.
        if (exception is System.Net.HttpListenerException)
        {
            return;
        }

        try
        {
            AppLogger.WriteStartup($"FirstChance {exception}");
        }
        catch
        {
            // Logging must never throw.
        }
    }

    private static void AppDomain_UnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        AppLogger.WriteStartup($"AppDomain.UnhandledException {exception}");
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            AppLogger.WriteStartup("OnLaunched start");
            _window = new MainWindow();
            _window.Activate();
            AppLogger.WriteStartup("OnLaunched success");
        }
        catch (Exception ex)
        {
            AppLogger.WriteStartup($"OnLaunched exception {ex}");
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLogger.WriteStartup($"UnhandledException {e.Exception}");

        // XAML re-throws managed exceptions through COM, which terminates the process
        // (E_INVALIDARG inside combase.dll). Marking the exception as handled keeps the
        // application alive and lets the user continue working; the details are logged.
        e.Handled = true;
    }
}
