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
        DataContextChanged += OnDataContextChanged;
        InitializeScroll();
        // The results view handles the click itself (it starts a selection), so listen to handled clicks too.
        ResultsView.AddHandler(MouseLeftButtonDownEvent, new System.Windows.Input.MouseButtonEventHandler(OnResultsMouseDown),
            handledEventsToo: true);
    }

    /// <summary>The truncation marker was clicked (ticket 07): open the detail pane on that line.</summary>
    private void OnFullLineRequested(object? sender, long line)
    {
        if (DataContext is LogTabViewModel tab) tab.Detail.CurrentLine = line;
        if (Window.GetWindow(this)?.DataContext is MainViewModel main) main.DetailPane.IsOpen = true;
    }

    /// <summary>The line-number margin was double-clicked (ticket 04): toggle the bookmark on that file line.</summary>
    private void OnMarginDoubleClicked(object? sender, long line)
    {
        if (DataContext is LogTabViewModel tab) tab.ToggleBookmarkAtCommand.Execute(line);
    }

    /// <summary>When this tab becomes the visible one, give its viewer the keyboard.</summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(() => LogView.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>Jumps (go to line, Ctrl+Home/End) scroll this tab's viewer to the caret (ticket 05).</summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LogTabViewModel old) old.Navigation.RevealRequested -= OnRevealRequested;
        if (e.NewValue is LogTabViewModel tab) tab.Navigation.RevealRequested += OnRevealRequested;
        AttachSearch(e.OldValue as LogTabViewModel, e.NewValue as LogTabViewModel);
        AttachScroll(e.NewValue as LogTabViewModel);
    }

    private void OnRevealRequested(object? sender, EventArgs e)
    {
        LogView.RevealCaret();
        if (IsVisible) LogView.Focus();
    }
}
