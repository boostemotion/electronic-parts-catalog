using System.Diagnostics;
using ComponentCatalogWinUI.Pages;
using ComponentCatalogWinUI.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ComponentCatalogWinUI;

public sealed partial class MainWindow : Window
{
    private bool _shutdownStarted;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppTitleBar.Title = "电子元件入库台";
        AppWindow.Closing += AppWindow_Closing;

        NavFrame.Navigated += NavFrame_Navigated;
        NavFrame.Navigate(typeof(HomePage), null, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        try
        {
            await CatalogWorkspaceService.Current.ShutdownAsync();
        }
        catch (Exception exception)
        {
            AppLogger.WriteStartup($"App.Shutdown.Error {exception.Message}");
        }
        finally
        {
            _allowClose = true;
            Close();
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack)
        {
            NavFrame.GoBack();
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type? targetPageType = null;

        if (args.IsSettingsSelected)
        {
            targetPageType = typeof(SettingsPage);
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "home":
                    targetPageType = typeof(HomePage);
                    break;
                case "import":
                    targetPageType = typeof(ImportPage);
                    break;
                case "about":
                    targetPageType = typeof(AboutPage);
                    break;
                default:
                    Debug.WriteLine($"警告：未识别的导航项 Tag: {item.Tag}");
                    break;
            }
        }

        if (targetPageType is null) return;

        // Rapid clicks land while the previous navigation is still tearing down its entrance
        // animation; navigating again at that moment crashes inside Microsoft.UI.Xaml. Queue the
        // latest target and apply it once the dispatcher drains instead of navigating re-entrantly.
        if (_navigationInFlight)
        {
            _pendingNavigationType = targetPageType;
            return;
        }

        _navigationInFlight = true;
        try
        {
            if (NavFrame.CurrentSourcePageType != targetPageType)
            {
                NavFrame.Navigate(targetPageType, null, args.RecommendedNavigationTransitionInfo);
            }
        }
        finally
        {
            NavFrame.DispatcherQueue.TryEnqueue(() =>
            {
                _navigationInFlight = false;
                if (_pendingNavigationType is { } pending && NavFrame.CurrentSourcePageType != pending)
                {
                    _pendingNavigationType = null;
                    NavFrame.Navigate(pending, null, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
                }
                else
                {
                    _pendingNavigationType = null;
                }
            });
        }
    }

    private bool _navigationInFlight;
    private Type? _pendingNavigationType;

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        AppTitleBar.IsBackButtonVisible = NavFrame.CanGoBack;
    }

}
