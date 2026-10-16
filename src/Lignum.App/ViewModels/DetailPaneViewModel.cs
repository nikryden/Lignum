using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Lignum.App.Viewer;

namespace Lignum.App.ViewModels;

/// <summary>
/// What one tab's detail pane shows: the current line in full, word-wrapped and read-only. Where the pane sits
/// (open, docked right or bottom, size) is app-wide: <see cref="DetailPaneLayoutViewModel"/>.
/// <para>
/// <see cref="CurrentLine"/> is the hook for the selection (ticket 08) and the caret (ticket 05): set it to
/// the 0-based file line to show. Text is read off the UI thread through <see cref="Reader"/>.
/// </para>
/// </summary>
public sealed partial class DetailPaneViewModel : ObservableObject
{
    /// <summary>
    /// Most characters put in the text box; a WPF TextBox wrapping megabytes blocks the UI thread in layout
    /// (1M chars took ~1.2 s in a smoke test). The rest is still copied by "Copy line".
    /// </summary>
    public const int MaxShownChars = 250_000;

    private const string NoLineHeader = "No line selected";

    private int _loadVersion;

    [ObservableProperty]
    private ILineReader? _reader;

    [ObservableProperty]
    private long? _currentLine;

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private string _header = NoLineHeader;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Notice))]
    private bool _isCapped;

    /// <summary>Completes when the text for the current line has been loaded (tests await it).</summary>
    public Task WhenLoaded { get; private set; } = Task.CompletedTask;

    /// <summary>Shown above the text when the line was too long to show completely.</summary>
    public string Notice => IsCapped
        ? $"Showing the first {MaxShownChars.ToString("N0", CultureInfo.CurrentCulture)} characters. Copy line copies all of it."
        : "";

    /// <summary>Reads the whole current line (for copying), however long. Null if there is none.</summary>
    public Task<string?> ReadFullLineAsync()
    {
        var reader = Reader;
        var line = CurrentLine;
        if (reader is null || line is not { } l) return Task.FromResult<string?>(null);
        return Task.Run(() => reader.ReadLines(l, 1) is [var text] ? text : null);
    }

    partial void OnReaderChanged(ILineReader? value)
    {
        // A new document (or a reload): the old line number means nothing there.
        if (CurrentLine is null) Load(null);
        else CurrentLine = null;
    }

    partial void OnCurrentLineChanged(long? value) => Load(value);

    private void Load(long? line) => WhenLoaded = LoadAsync(line, ++_loadVersion);

    private async Task LoadAsync(long? line, int version)
    {
        var reader = Reader;
        if (reader is null || line is not { } l)
        {
            Show(NoLineHeader, "", false);
            return;
        }

        var header = $"Line {(l + 1).ToString("N0", CultureInfo.CurrentCulture)}";
        try
        {
            var prefixes = await Task.Run(() => reader.ReadLinePrefixes(l, 1, MaxShownChars));
            if (version != _loadVersion) return; // another line was chosen meanwhile
            if (prefixes is [var prefix]) Show(header, prefix.Text, prefix.IsTruncated);
            else Show($"{header} is no longer available", "", false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or ArgumentOutOfRangeException)
        {
            if (version == _loadVersion) Show($"{header} is no longer available", "", false);
        }
    }

    private void Show(string header, string text, bool capped)
    {
        Header = header;
        Text = text;
        IsCapped = capped;
    }
}
