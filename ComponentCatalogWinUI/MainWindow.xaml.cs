using System.Diagnostics;
using ComponentCatalogWinUI.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ComponentCatalogWinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppTitleBar.Title = "电子元件入库台";

        NavFrame.Navigated += NavFrame_Navigated;
        NavFrame.Navigate(typeof(HomePage));
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
        
        // 避免重复导航导致向后退栈(BackStack)中压入重复页面，从而出现多余的返回箭头
        if (NavFrame.CurrentSourcePageType != targetPageType)
        {
            NavFrame.Navigate(targetPageType);
        }
    }

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        AppTitleBar.IsBackButtonVisible = NavFrame.CanGoBack;
    }

}
