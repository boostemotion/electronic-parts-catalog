using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ComponentCatalogWinUI.Converters;

public sealed class SelectedCardValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isSelected = value is true;

        return parameter switch
        {
            "Brush" => Application.Current.Resources[
                isSelected ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush"],
            "Thickness" => new Thickness(isSelected ? 2 : 1),
            _ => throw new ArgumentException("Expected Brush or Thickness.", nameof(parameter)),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
