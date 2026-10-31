using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OculusGoAirMouse.Services;

public sealed class WpfLanguageTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 0 || values[0] == DependencyProperty.UnsetValue ? "" : Localization.Current.Translate(values[0]?.ToString() ?? "");
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("Localized display text is read-only.");
}
