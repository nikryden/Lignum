using System.Globalization;
using System.Windows;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

/// <summary>Asks for a 1-based line number and moves the caret there through <see cref="NavigationViewModel"/>.</summary>
public partial class GoToLineDialog : Window
{
    private readonly NavigationViewModel _navigation;

    public GoToLineDialog(NavigationViewModel navigation, long lineCount)
    {
        _navigation = navigation;
        InitializeComponent();
        Prompt.Text = string.Format(CultureInfo.CurrentCulture, "Line number (1 - {0:N0})", Math.Max(1, lineCount));
        LineBox.Text = (navigation.Caret.Line + 1).ToString(CultureInfo.InvariantCulture);
        Loaded += (_, _) =>
        {
            LineBox.Focus();
            LineBox.SelectAll();
        };
    }

    private void OnGoClick(object sender, RoutedEventArgs e)
    {
        if (_navigation.TryGoToLine(LineBox.Text, out var error))
        {
            DialogResult = true;
            return;
        }

        ErrorText.Text = error;
        ErrorText.Visibility = Visibility.Visible;
        LineBox.Focus();
        LineBox.SelectAll();
    }
}
