using System.Windows;
using System.Windows.Controls;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

public partial class LogTabView : UserControl
{
    public LogTabView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    // ---- detail pane (ticket 07). Clicking a row stands in for the caret/selection until tickets 05/08. ----

    private void OnLogViewLineClicked(object? sender, long line)
    {
        if (DataContext is LogTabViewModel tab) tab.Detail.CurrentLine = line;
    }

    private void OnFullLineRequested(object? sender, long line)
    {
        if (DataContext is LogTabViewModel tab) tab.Detail.CurrentLine = line;
        if (Window.GetWindow(this)?.DataContext is MainViewModel main) main.DetailPane.IsOpen = true;
    }

    /// <summary>When this tab becomes the visible one, give its viewer the keyboard.</summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(() => LogView.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}
