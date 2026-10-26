using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

// The bookmarks pane's note editor and links (ticket 23): Ctrl+click opens a link, in the list rows and in the
// editor; the editor's right-click menu adds the link's actions to cut, copy and paste.
public partial class LogTabView
{
    private static bool CtrlDown => (Keyboard.Modifiers & ModifierKeys.Control) != 0;

    /// <summary>A link in a list row: Ctrl+click opens it; a plain click only selects the row.</summary>
    private void OnNoteLinkClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Hyperlink { Tag: NoteLink link } || !CtrlDown) return;
        if (DataContext is LogTabViewModel tab) tab.OpenNoteLinkCommand.Execute(link);
        e.Handled = true;
    }

    private void OnNoteBoxMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!CtrlDown || LinkAt(e.GetPosition(BookmarkNoteBox)) is not { } link) return;
        if (DataContext is LogTabViewModel tab) tab.OpenNoteLinkCommand.Execute(link);
        e.Handled = true;
    }

    /// <summary>Ctrl+Enter saves the note; Enter alone is a new line.</summary>
    private void OnNoteBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !CtrlDown) return;
        BookmarkNoteBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    /// <summary>Cut, copy and paste, plus the link's actions when the menu opens on a link (or the caret is on one).</summary>
    private void OnNoteBoxContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (BookmarkNoteBox.ContextMenu is not { } menu || DataContext is not LogTabViewModel tab) return;
        menu.Items.Clear();
        menu.Items.Add(new MenuItem { Header = "Cu_t", Command = ApplicationCommands.Cut, CommandTarget = BookmarkNoteBox });
        menu.Items.Add(new MenuItem { Header = "_Copy", Command = ApplicationCommands.Copy, CommandTarget = BookmarkNoteBox });
        menu.Items.Add(new MenuItem { Header = "_Paste", Command = ApplicationCommands.Paste, CommandTarget = BookmarkNoteBox });

        var link = (e.CursorLeft >= 0 ? LinkAt(new Point(e.CursorLeft, e.CursorTop)) : null)
                   ?? NoteLinks.At(BookmarkNoteBox.Text, BookmarkNoteBox.CaretIndex);
        if (link is null) return;
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = link.Kind == NoteLinkKind.Web ? "_Open in browser" : "_Open",
            Command = tab.OpenNoteLinkCommand, CommandParameter = link,
        });
        if (link.Kind == NoteLinkKind.Path)
            menu.Items.Add(new MenuItem { Header = "Show in _folder", Command = tab.ShowNoteLinkInFolderCommand, CommandParameter = link });
        menu.Items.Add(new MenuItem { Header = "Copy _link", Command = tab.CopyNoteLinkCommand, CommandParameter = link });
    }

    /// <summary>The link under <paramref name="point"/> (in the note box), or null.</summary>
    private NoteLink? LinkAt(Point point)
    {
        var index = BookmarkNoteBox.GetCharacterIndexFromPoint(point, snapToText: false);
        return index < 0 ? null : NoteLinks.At(BookmarkNoteBox.Text, index);
    }
}
