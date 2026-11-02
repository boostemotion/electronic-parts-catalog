using ComponentCatalogWinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ComponentCatalogWinUI.Converters;

public sealed class SelectedCardValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isSelected = value is true;

        try
        {
            return parameter switch
            {
                "Brush" => ResolveBrush(isSelected ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush", isSelected),
                "Thickness" => new Thickness(isSelected ? 2 : 1),
                _ => throw new ArgumentException("Expected Brush or Thickness.", nameof(parameter)),
            };
        }
        catch (Exception exception)
        {
            // A converter runs inside the XAML binding engine. An exception here is re-thrown
            // through COM and terminates the process (E_INVALIDARG inside combase.dll), so the
            // lookup must never fail.
            AppLogger.WritePage($"SelectedCardValueConverter error: {exception.Message}");
            return string.Equals(parameter as string, "Thickness", StringComparison.Ordinal)
                ? new Thickness(1)
                : FallbackBrush(isSelected);
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    private static Brush ResolveBrush(string key, bool isSelected)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var resource) == true && resource is Brush brush)
        {
            return brush;
        }

        return FallbackBrush(isSelected);
    }

    private static Brush FallbackBrush(bool isSelected)
    {
        return isSelected
            ? new SolidColorBrush(Microsoft.UI.Colors.IndianRed)
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}
