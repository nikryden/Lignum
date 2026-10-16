using System.Windows;
using System.Windows.Controls;

namespace Lignum.App.Views;

public partial class LogTabView : UserControl
{
    public LogTabView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
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
