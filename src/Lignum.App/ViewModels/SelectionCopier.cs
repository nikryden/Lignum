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

    public async Task<CopyResult> CopyAsync(LogDocument document, TextSelection selection,
        CancellationToken cancellationToken = default)
    {
        if (selection.IsEmpty) return new CopyResult(CopyOutcome.NothingSelected, "Nothing selected");
        var start = selection.Start;
        var end = selection.End;

        var choice = LargeCopyChoice.Copy;
        var size = document.MeasureTextRange(start, end);
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
                        document.ReadTextRange(start, end, writer, cancellationToken);
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
                        document.ReadTextRange(start, end, writer, cancellationToken);
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
