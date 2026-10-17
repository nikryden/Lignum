using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Viewer;
using Lignum.Core;

namespace Lignum.App.ViewModels;

// The right-click menu and Ctrl+Shift+C (ticket 15). Every "selected text" action takes its text from
// PickSearchTextAsync: the selection, or its first line, or the word under the cursor.
public sealed partial class LogTabViewModel
{
    private HighlightingViewModel? Highlighting => _services.Highlighting;

    /// <summary>The picked text is cut to this many characters (a selection inside a multi-megabyte line).</summary>
    internal const int MaxSearchTextLength = 10_000;

    /// <summary>
    /// Ctrl+Shift+C / "Copy to search": puts the picked text in the find box and runs find all.
    /// <paramref name="selection"/> is the view's selection (the results pane passes its own); null = the main view's.
    /// </summary>
    [RelayCommand]
    private async Task CopyToSearch(TextSelection? selection)
    {
        if (await PickSearchTextAsync(selection ?? Selection) is not { } text) return;
        // Keep the user's find options; with regex on, escape the text so it still matches literally.
        Search.FindText = Search.IsRegex ? Regex.Escape(text) : text;
        Search.IsFindBarOpen = true;
        await Search.FindAllCommand.ExecuteAsync(null);
    }

    /// <summary>"Add as include filter": a plain-text include rule for the picked text (see <see cref="CopyToSearch"/>).</summary>
    [RelayCommand]
    private Task AddIncludeFilter(TextSelection? selection) => AddFilter(selection, RuleKind.Include);

    /// <summary>"Add as exclude filter": a plain-text exclude rule for the picked text.</summary>
    [RelayCommand]
    private Task AddExcludeFilter(TextSelection? selection) => AddFilter(selection, RuleKind.Exclude);

    private async Task AddFilter(TextSelection? selection, RuleKind kind)
    {
        if (await PickSearchTextAsync(selection ?? Selection) is not { } text) return;
        Search.AddFilterRule(new QueryRule(text, kind));
        Search.IsFilterBarOpen = true; // show the new rule so it can be edited or removed
    }

    /// <summary>"Add as highlight rule": a plain-text rule at the top of the active highlight preset.</summary>
    [RelayCommand]
    private async Task AddHighlightRule(TextSelection? selection)
    {
        if (Highlighting is not { } highlighting) return;
        if (await PickSearchTextAsync(selection ?? Selection) is not { } text) return;
        highlighting.AddRule(text);
    }

    /// <summary>
    /// The text the menu actions work on: the selected text, only its first line when it spans lines, or the
    /// word under the caret when nothing is selected. Null when there is nothing usable (blank, unreadable).
    /// Reads the file in the background.
    /// </summary>
    public async Task<string?> PickSearchTextAsync(TextSelection selection)
    {
        if (_document is not { } document) return null;
        if (selection.IsEmpty) return await WordAtAsync(document, selection.Caret);
        var start = selection.Start;
        // Only the first line, and never more than MaxSearchTextLength characters of it (a huge line).
        var endColumn = selection.End.Line == start.Line ? selection.End.Column : int.MaxValue;
        var end = new FilePosition(start.Line, (int)Math.Min(endColumn, (long)start.Column + MaxSearchTextLength));
        try
        {
            var text = await Task.Run(() =>
            {
                using var writer = new StringWriter();
                document.ReadTextRange(start, end, writer, CancellationToken.None);
                return writer.ToString().TrimEnd('\r', '\n');
            });
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IOException)
        {
            return null; // the line is gone (truncated or replaced file) or not indexed yet
        }
    }

    /// <summary>The word (letters, digits, '_') at or just before <paramref name="caret"/>; null if there is none.</summary>
    private static async Task<string?> WordAtAsync(LogDocument document, FilePosition caret)
    {
        try
        {
            // A prefix is enough: the word ends at most MaxSearchTextLength characters after the caret.
            var maxChars = (int)Math.Min(int.MaxValue, (long)caret.Column + MaxSearchTextLength);
            var prefix = await Task.Run(() => document.ReadLinePrefix(caret.Line, maxChars));
            return WordAt(prefix.Text, caret.Column);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IOException)
        {
            return null;
        }
    }

    internal static string? WordAt(string text, int index)
    {
        index = Math.Min(index, text.Length);
        // On a word, or right after one (the caret sits after the last character of a double-clicked word).
        if (!(index < text.Length && IsWordChar(text[index])) && !(index > 0 && IsWordChar(text[index - 1])))
            return null;
        var from = index;
        while (from > 0 && IsWordChar(text[from - 1])) from--;
        var to = index;
        while (to < text.Length && IsWordChar(text[to])) to++;
        return text[from..to];
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
