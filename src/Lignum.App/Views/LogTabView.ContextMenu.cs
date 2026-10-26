using System.Windows;
using System.Windows.Controls;
using Lignum.App.Settings;
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

    /// <summary>"Open in editor at this line" (ticket 13): the caret line of the view the menu was opened on.</summary>
    private void OnOpenInEditorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogTabViewModel tab) return;
        if (Window.GetWindow(this)?.DataContext is not MainViewModel main) return;
        var target = ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget;
        var selection = ReferenceEquals(target, ResultsView) ? tab.Search.ResultsSelection : tab.Selection;
        main.OpenInEditorCommand.Execute(selection.Caret.Line); // this view's tab is the selected one
    }

    /// <summary>Most lines one "Highlight line…" colors: a highlight is stored per line, so Select All on a huge log must not.</summary>
    private const long MaxHighlightLines = 10_000;

    /// <summary>"Highlight line…" (ticket 14): colors the selected lines of the main view, or the caret line.</summary>
    private void OnHighlightLineClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogTabViewModel tab || HighlightTargetLines(sender, tab) is not { } lines) return;
        var dialog = new LineHighlightDialog(lines.Count, tab.Bookmarks.HighlightFor(lines[0])) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) tab.Bookmarks.SetHighlight(lines, dialog.LineForeground, dialog.LineBackground);
    }

    /// <summary>"Remove highlight" (ticket 14) on the same lines "Highlight line…" would color.</summary>
    private void OnRemoveHighlightClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is LogTabViewModel tab && HighlightTargetLines(sender, tab) is { } lines) tab.Bookmarks.RemoveHighlight(lines);
    }

    /// <summary>The main view's selected lines (a selection ending at column 0 stops on the line before), else the caret line.</summary>
    private List<long>? HighlightTargetLines(object sender, LogTabViewModel tab)
    {
        var target = ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget;
        if (ReferenceEquals(target, ResultsView)) return [tab.Search.ResultsSelection.Caret.Line];
        var selection = tab.Selection;
        var (first, last) = (selection.Start.Line, selection.End.Line);
        if (last > first && selection.End.Column == 0) last--;
        if (last - first + 1 > MaxHighlightLines)
        {
            MessageBox.Show(Window.GetWindow(this)!, $"Select at most {MaxHighlightLines:N0} lines to highlight by hand. " +
                "For more, add a highlight rule (View → Highlight rules…).", "Highlight line",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var lines = new List<long>();
        for (var line = first; line <= last; line++) lines.Add(line);
        return lines;
    }

    /// <summary>"Edit description…" (ticket 06; the bookmark note) on the caret line of the view the menu was opened on.</summary>
    private void OnEditNoteClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogTabViewModel tab) return;
        var target = ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget;
        var line = (ReferenceEquals(target, ResultsView) ? tab.Search.ResultsSelection : tab.Selection).Caret.Line;
        var dialog = new BookmarkNoteDialog(line, tab.Bookmarks.NoteFor(line)) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) tab.Bookmarks.SetNote(line, dialog.Note);
    }

    /// <summary>"Bookmark color" → a color (its Tag) on the caret line of the view the menu was opened on.</summary>
    private void OnBookmarkColorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogTabViewModel tab || sender is not MenuItem { Tag: string name }) return;
        if (!Enum.TryParse<BookmarkColor>(name, out var color)) return;
        // The item sits in a submenu: walk up to the context menu to find which view it was opened on.
        DependencyObject? node = sender as MenuItem;
        while (node is not null and not System.Windows.Controls.ContextMenu) node = LogicalTreeHelper.GetParent(node);
        var target = (node as ContextMenu)?.PlacementTarget;
        var line = (ReferenceEquals(target, ResultsView) ? tab.Search.ResultsSelection : tab.Selection).Caret.Line;
        tab.Bookmarks.SetColor(line, color);
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
