using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

public partial class AllTabsSearchPane : UserControl
{
    public AllTabsSearchPane() => InitializeComponent();

    /// <summary>The find box and options (the Find window shows them; the main window's list does not).</summary>
    public bool ShowControls
    {
        get => ControlsPanel.Visibility == Visibility.Visible;
        set => ControlsPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The results list (the main window shows it; the Find window does not).</summary>
    public bool ShowResults
    {
        get => ResultList.Visibility == Visibility.Visible;
        set
        {
            ResultList.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            CloseListButton.Visibility = ResultList.Visibility;
        }
    }

    public void FocusFindBox() =>
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
        if (DataContext is not AllTabsSearchViewModel search) return;
        if (ResultList.SelectedItem is AllTabsFileRow file) search.ToggleGroupCommand.Execute(file);
        else if (ResultList.SelectedItem is AllTabsMatchRow match) search.GoToCommand.Execute(match);
    }
}
