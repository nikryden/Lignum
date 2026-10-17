using System.Globalization;
using System.Windows;

namespace Lignum.App.Views;

/// <summary>Asks for the note on one line's bookmark. <see cref="Note"/> is the text after OK.</summary>
public partial class BookmarkNoteDialog : Window
{
    public BookmarkNoteDialog(long line, string? note)
    {
        InitializeComponent();
        Prompt.Text = string.Format(CultureInfo.CurrentCulture, "Note for line {0:N0}", line + 1);
        NoteBox.Text = note ?? "";
        Loaded += (_, _) =>
        {
            NoteBox.Focus();
            NoteBox.SelectAll();
        };
    }

    public string Note => NoteBox.Text;

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
