using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

public partial class AllTabsSearchPane : UserControl
{
    public AllTabsSearchPane()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is AllTabsSearchViewModel old) old.FindBoxFocusRequested -= OnFocusRequested;
            if (e.NewValue is AllTabsSearchViewModel search) search.FindBoxFocusRequested += OnFocusRequested;
        };
    }

    private void OnFocusRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            FindBox.Focus();
            FindBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);

    private void OnResultClick(object sender, MouseButtonEventArgs e) => GoToSelected();

    private void OnResultKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        GoToSelected();
        e.Handled = true;
    }

    private void GoToSelected()
    {
        if (DataContext is AllTabsSearchViewModel search && ResultList.SelectedItem is AllTabsRow row)
            search.GoToCommand.Execute(row);
    }
}
