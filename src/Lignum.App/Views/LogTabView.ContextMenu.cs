using System.Windows;
using System.Windows.Controls;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

// Right-click menus (ticket 15): the text items are commands on LogTabViewModel (LogTabViewModel.ContextMenu.cs);
// "Show full line" needs the app-wide detail pane layout, so it goes through MainViewModel here.
public partial class LogTabView
{
    /// <summary>"Show full line": the detail pane on the caret line of the view the menu was opened on.</summary>
    private void OnShowFullLineClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogTabViewModel tab) return;
        if (Window.GetWindow(this)?.DataContext is not MainViewModel main) return;
        var target = ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget;
        var selection = ReferenceEquals(target, ResultsView) ? tab.Search.ResultsSelection : tab.Selection;
        main.ShowFullLine(selection.Caret.Line); // this view's tab is the selected one
    }
}
