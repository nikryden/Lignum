using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

// The bookmarks pane's right-click menu, links and resizing (ticket 23). A description is edited in the same dialog
// as "Edit description…" in the log view. Ctrl+click on a link in a row opens it: a web address in the browser, a path
// in Explorer (a file selected in its folder); the right-click menu also has "Open in tab" for paths.
public partial class LogTabView
{
    /// <summary>A link in a row: Ctrl+click opens it; a plain click only selects the row.</summary>
    private void OnNoteLinkClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Hyperlink { Tag: NoteLink link } || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (DataContext is LogTabViewModel tab) tab.OpenNoteLinkCommand.Execute(link);
        e.Handled = true;
    }

    /// <summary>Adds an "Open" (and for a path "Open in tab") item per link in the selected bookmark's description.</summary>
    private void OnBookmarkListMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || DataContext is not LogTabViewModel tab) return;
        while (menu.Items.Count > 3) menu.Items.RemoveAt(3); // the fixed items stay
        var links = NoteLinks.Find((BookmarkList.SelectedItem as BookmarkItem)?.Note);
        if (links.Count == 0) return;
        menu.Items.Add(new Separator());
        foreach (var link in links)
        {
            // Headers are plain text, so an underscore in a path is not an access key.
            var open = link.Kind == NoteLinkKind.Web ? $"Open {link.Target}" : $"Show {link.Target} in Explorer";
            menu.Items.Add(new MenuItem { Header = new TextBlock { Text = open },
                                          Command = tab.OpenNoteLinkCommand, CommandParameter = link });
            if (link.Kind == NoteLinkKind.Path)
                menu.Items.Add(new MenuItem { Header = new TextBlock { Text = $"Open {link.Target} in a tab" },
                                              Command = tab.OpenNoteLinkInTabCommand, CommandParameter = link });
        }
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
