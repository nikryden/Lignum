using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

/// <summary>
/// Shows the current line in full, wrapped and read-only. Ctrl+C in the text copies the selection; "Copy line"
/// copies the whole line (read again off the UI thread, so it works past the text box cap).
/// </summary>
public partial class DetailPane : UserControl
{
    public DetailPane()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is DetailPaneViewModel old) old.PropertyChanged -= OnViewModelPropertyChanged;
            if (e.NewValue is DetailPaneViewModel vm) vm.PropertyChanged += OnViewModelPropertyChanged;
        };
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DetailPaneViewModel.Text)) LineText.ScrollToHome();
    }

    private async void OnCopyLineClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DetailPaneViewModel vm) return;
        try
        {
            if (await vm.ReadFullLineAsync() is { } text) Clipboard.SetText(text);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or ArgumentOutOfRangeException
                                       or COMException or OutOfMemoryException)
        {
            // The line is gone (file changed) or the clipboard is busy/too small: nothing sensible to do.
        }
    }
}
