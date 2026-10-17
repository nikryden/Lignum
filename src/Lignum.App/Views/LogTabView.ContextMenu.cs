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

    /// <summary>"Edit bookmark note…" (ticket 06) on the caret line of the view the menu was opened on.</summary>
    private void OnEditNoteClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogTabViewModel tab) return;
        var target = ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget;
        var line = (ReferenceEquals(target, ResultsView) ? tab.Search.ResultsSelection : tab.Selection).Caret.Line;
        var dialog = new BookmarkNoteDialog(line, tab.Bookmarks.NoteFor(line)) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) tab.Bookmarks.SetNote(line, dialog.Note);
    }

    /// <summary>A click on a bookmarks pane row jumps to its line and gives the viewer the keyboard.</summary>
    private void OnBookmarkListClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => GoToSelectedBookmark();

    private void OnBookmarkListKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        GoToSelectedBookmark();
        e.Handled = true;
    }

    private void GoToSelectedBookmark()
    {
        if (DataContext is not LogTabViewModel tab || BookmarkList.SelectedItem is not BookmarkItem item) return;
        tab.GoToBookmarkCommand.Execute(item);
        LogView.Focus();
    }
}
