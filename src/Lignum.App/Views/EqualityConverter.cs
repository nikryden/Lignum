using System.Globalization;
using System.Windows.Data;

namespace Lignum.App.Views;

/// <summary>True when the bound value equals the ConverterParameter (e.g. to check the current theme in a menu).</summary>
public sealed class EqualityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value, parameter) || (value is not null && parameter is string s
                                     && string.Equals(value.ToString(), s, StringComparison.Ordinal));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
