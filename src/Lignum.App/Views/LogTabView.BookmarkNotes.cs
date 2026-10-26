using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

// The bookmarks pane's right-click menu, links and resizing (ticket 23). A description is edited in the same dialog
// as "Edit description…" in the log view; Ctrl+click on a web link in a row opens it in the browser.
public partial class LogTabView
{
    /// <summary>A link in a row: Ctrl+click opens it; a plain click only selects the row.</summary>
    private void OnNoteLinkClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Hyperlink { Tag: NoteLink link } || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (DataContext is LogTabViewModel tab) tab.OpenNoteLinkCommand.Execute(link);
        e.Handled = true;
    }

    private void OnEditBookmarkDescriptionClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogTabViewModel tab || BookmarkList.SelectedItem is not BookmarkItem item) return;
        var dialog = new BookmarkNoteDialog(item.Line, item.Note) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) tab.Bookmarks.SetNote(item.Line, dialog.Note);
    }

    private void OnCopyBookmarkDescriptionClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is LogTabViewModel tab) tab.CopyBookmarkNoteCommand.Execute(BookmarkList.SelectedItem as BookmarkItem);
    }

    private void OnRemoveBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is LogTabViewModel tab && BookmarkList.SelectedItem is BookmarkItem item) tab.Bookmarks.Remove([item.Line]);
    }

    /// <summary>Dragging the pane's left edge: left makes it wider. At most two thirds of the tab.</summary>
    private void OnBookmarksPaneResize(object sender, DragDeltaEventArgs e) =>
        BookmarksPane.SetCurrentValue(WidthProperty, Math.Clamp(BookmarksPane.Width - e.HorizontalChange, BookmarksPane.MinWidth,
            Math.Max(BookmarksPane.MinWidth, ActualWidth * 2 / 3))); // keeps the binding to the saved width
}
