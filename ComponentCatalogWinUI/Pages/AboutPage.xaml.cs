using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace ComponentCatalogWinUI.Pages;

public sealed partial class AboutPage : Page
{
    public string AppVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version is { } version
        ? $"{version.Major}.{version.Minor}"
        : "1.1";

    public AboutPage()
    {
        InitializeComponent();
    }
}
