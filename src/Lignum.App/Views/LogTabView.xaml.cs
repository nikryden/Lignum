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
    }

    private void OnRevealRequested(object? sender, EventArgs e)
    {
        LogView.RevealCaret();
        if (IsVisible) LogView.Focus();
    }
}
