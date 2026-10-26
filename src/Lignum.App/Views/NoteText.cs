using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

/// <summary>
/// <c>views:NoteText.Note="{Binding Note}"</c> on a TextBlock shows a bookmark note with its links (ticket 23) as
/// hyperlinks. Each hyperlink's Tag is its <see cref="NoteLink"/>; the pane handles <see cref="Hyperlink.ClickEvent"/>
/// and opens it on Ctrl+click.
/// </summary>
public static class NoteText
{
    public static readonly DependencyProperty NoteProperty = DependencyProperty.RegisterAttached(
        "Note", typeof(string), typeof(NoteText), new PropertyMetadata(null, OnNoteChanged));

    public static string? GetNote(TextBlock block) => (string?)block.GetValue(NoteProperty);

    public static void SetNote(TextBlock block, string? value) => block.SetValue(NoteProperty, value);

    private static void OnNoteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        block.Inlines.Clear();
        var note = (string?)e.NewValue ?? "";
        var at = 0;
        foreach (var link in NoteLinks.Find(note))
        {
            if (link.Start > at) block.Inlines.Add(new Run(note[at..link.Start]));
            block.Inlines.Add(new Hyperlink(new Run(note.Substring(link.Start, link.Length)))
            {
                Tag = link,
                ToolTip = $"Ctrl+click to open {link.Target}",
            });
            at = link.Start + link.Length;
        }

        if (at < note.Length) block.Inlines.Add(new Run(note[at..]));
    }
}
