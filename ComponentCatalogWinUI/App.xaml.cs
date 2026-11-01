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
        UnhandledException += App_UnhandledException;
        AppLogger.WriteStartup("App ctor");
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
    }
}
