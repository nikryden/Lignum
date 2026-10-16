using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace Lignum.App.Views;

/// <summary>Collapsed for null or an empty string, visible otherwise (the find box's error line).</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null or "" ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The find box shown above the viewer by Ctrl+F.</summary>
public partial class FindBar : UserControl
{
    public FindBar() => InitializeComponent();

    /// <summary>Puts the caret in the find box with its text selected, ready to type over.</summary>
    public void FocusFindBox()
    {
        // The bar may only just have become visible; focus once layout has run.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            FindBox.Focus();
            FindBox.SelectAll();
        });
    }
}
