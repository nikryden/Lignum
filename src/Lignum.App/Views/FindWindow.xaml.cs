using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Lignum.App.Views;

/// <summary>The pages of the <see cref="FindWindow"/>.</summary>
public enum FindPage
{
    Find,
    Filter,
    AllTabs,
}

/// <summary>Visible for null, collapsed otherwise ("open a file" hints).</summary>
public sealed class NullToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Find, filter rules and find in all tabs in one tool window: above Lignum (its owner) but not above other
/// apps, never blocking the main window. Closing hides it, so it keeps its page and position.
/// </summary>
public partial class FindWindow : Window
{
    private bool _closing;

    public FindWindow()
    {
        Resources.Add("NullToVisible", new NullToVisibleConverter());
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            Hide();
            e.Handled = true;
        };
    }

    public FindPage Page => (FindPage)Pages.SelectedIndex;

    /// <summary>Shows the window on <paramref name="page"/> with the keyboard in that page's box.</summary>
    public void ShowPage(FindPage page)
    {
        if (!IsVisible) Show();
        Activate();
        Pages.SelectedIndex = (int)page;
        switch (page)
        {
            case FindPage.Find:
                FindBar.FocusFindBox();
                break;
            case FindPage.Filter:
                FilterBar.FocusNewRuleBox();
                break;
            case FindPage.AllTabs:
                AllTabs.FocusFindBox();
                break;
        }
    }

    /// <summary>Closes for good (the main window is closing); otherwise closing only hides.</summary>
    public void CloseForGood()
    {
        _closing = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closing)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
