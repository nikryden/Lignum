using System.Text;
using Lignum.App.Viewer;
using Lignum.Core;

namespace Lignum.App.ViewModels;

/// <summary>Puts text on the clipboard. Called on the UI thread.</summary>
public interface IClipboardService
{
    void SetText(string text);
}

/// <summary>What to do with a selection above the large-copy threshold.</summary>
public enum LargeCopyChoice
{
    Cancel,
    Copy,
    SaveToFile,
}

/// <summary>Asks the user about large copies. Called on the UI thread.</summary>
public interface ICopyPrompt
{
    /// <summary>The selection is about <paramref name="approximateBytes"/> long: copy it, save it to a file or cancel?</summary>
    LargeCopyChoice AskLargeCopy(long approximateBytes);

    /// <summary>Where to save the selection; null if the user cancelled.</summary>
    string? AskSavePath();
}

public enum CopyOutcome
{
    NothingSelected,
    Copied,
    Saved,
    Cancelled,
    Failed,
}

/// <summary>How a copy ended; <see cref="Message"/> is a short status line for the user.</summary>
public sealed record CopyResult(CopyOutcome Outcome, string Message);

/// <summary>
/// Copies a <see cref="TextSelection"/> of a document. The text is streamed through
/// <see cref="LogDocument.ReadTextRange"/> on a background thread; only the clipboard call and the prompts
/// happen on the caller's (UI) thread. Above <see cref="Threshold"/> bytes the user chooses between copying,
/// saving to a file (streamed straight to disk) and cancelling.
/// </summary>
public sealed class SelectionCopier(IClipboardService clipboard, ICopyPrompt prompt, long threshold = SelectionCopier.DefaultThreshold)
{
    public const long DefaultThreshold = 50L * 1024 * 1024;

    public long Threshold { get; } = threshold;

    public Task<CopyResult> CopyAsync(LogDocument document, TextSelection selection,
        CancellationToken cancellationToken = default)
    {
        if (selection.IsEmpty) return Task.FromResult(NothingSelected);
        var start = selection.Start;
        var end = selection.End;
        return CopyCoreAsync(document.MeasureTextRange(start, end),
            (writer, token) => document.ReadTextRange(start, end, writer, token), cancellationToken);
    }

    /// <summary>
    /// Copies the part of a result set (find all / filter rows: sorted file lines) inside
    /// <paramref name="selection"/>: only the listed lines, joined with <see cref="Environment.NewLine"/>,
    /// the first and last cut at the selection's columns. The lines in between that are not results are skipped.
    /// </summary>
    public Task<CopyResult> CopyRowsAsync(LogDocument document, ILineList rows, TextSelection selection,
        CancellationToken cancellationToken = default)
    {
        if (selection.IsEmpty) return Task.FromResult(NothingSelected);
        var start = selection.Start;
        var end = selection.End;
        var firstRow = LowerBound(rows, start.Line);
        var endRow = LowerBound(rows, end.Line + 1); // exclusive
        if (firstRow >= endRow) return Task.FromResult(NothingSelected);

        // From the line index alone (no I/O): the lines' bytes plus a line break each.
        long size = 0;
        for (var row = firstRow; row < endRow && size <= Threshold; row++)
        {
            var line = rows[row];
            size += document.MeasureTextRange(new FilePosition(line, 0), new FilePosition(line, int.MaxValue)) + 2;
        }

        return CopyCoreAsync(size, (writer, token) =>
        {
            for (var row = firstRow; row < endRow; row++)
            {
                token.ThrowIfCancellationRequested();
                var line = rows[row];
                if (row > firstRow) writer.Write(Environment.NewLine);
                var from = line == start.Line ? start.Column : 0;
                var to = line == end.Line ? end.Column : int.MaxValue;
                if (to > from) document.ReadTextRange(new FilePosition(line, from), new FilePosition(line, to), writer, token);
            }
        }, cancellationToken);
    }

    private static readonly CopyResult NothingSelected = new(CopyOutcome.NothingSelected, "Nothing selected");

    /// <summary>The first row whose line is at or after <paramref name="line"/> (rows are sorted by line).</summary>
    private static long LowerBound(ILineList rows, long line)
    {
        long lo = 0, hi = rows.Count;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (rows[mid] < line) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }

    /// <summary>Asks about a large copy, then runs <paramref name="write"/> off the UI thread into the clipboard or a file.</summary>
    private async Task<CopyResult> CopyCoreAsync(long size, Action<TextWriter, CancellationToken> write,
        CancellationToken cancellationToken)
    {
        var choice = LargeCopyChoice.Copy;
        if (size > Threshold) choice = prompt.AskLargeCopy(size);

        try
        {
            switch (choice)
            {
                case LargeCopyChoice.Copy:
                {
                    var text = await Task.Run(() =>
                    {
                        var writer = new StringWriter();
                        write(writer, cancellationToken);
                        return writer.ToString();
                    }, cancellationToken);
                    clipboard.SetText(text); // back on the caller's thread (UI: the clipboard needs STA)
                    return new CopyResult(CopyOutcome.Copied, $"Copied {text.Length:N0} characters");
                }
                case LargeCopyChoice.SaveToFile:
                {
                    var path = prompt.AskSavePath();
                    if (path is null) return new CopyResult(CopyOutcome.Cancelled, "Copy cancelled");
                    await Task.Run(() =>
                    {
                        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
                        write(writer, cancellationToken);
                    }, cancellationToken);
                    return new CopyResult(CopyOutcome.Saved, $"Saved selection to {Path.GetFileName(path)}");
                }
                default:
                    return new CopyResult(CopyOutcome.Cancelled, "Copy cancelled");
            }
        }
        catch (OperationCanceledException)
        {
            return new CopyResult(CopyOutcome.Cancelled, "Copy cancelled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException
                                       or ObjectDisposedException or OutOfMemoryException
                                       or System.Runtime.InteropServices.ExternalException)
        {
            return new CopyResult(CopyOutcome.Failed, $"Copy failed: {ex.Message}");
        }
    }
}
