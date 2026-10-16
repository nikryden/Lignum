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
        DataContextChanged += (_, e) => AttachSearch(e.OldValue as LogTabViewModel, e.NewValue as LogTabViewModel);
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
