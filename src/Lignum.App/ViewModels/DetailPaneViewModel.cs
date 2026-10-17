using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Lignum.App.Viewer;
using Lignum.Core;

namespace Lignum.App.ViewModels;

/// <summary>
/// What one tab's detail pane shows: the current line in full, or every selected line when the selection
/// spans several (<see cref="ShowRange"/>), word-wrapped and read-only. Where the pane sits
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

    /// <summary>Most lines shown (and copied by "Copy lines") for a multi-line selection.</summary>
    public const int MaxShownLines = 1_000;

    private const string NoLineHeader = "No line selected";

    private int _loadVersion;

    [ObservableProperty]
    private ILineReader? _reader;

    [ObservableProperty]
    private long? _currentLine;

    /// <summary>The selected file lines (first, last) when the selection spans several lines; else null.</summary>
    private (long First, long Last)? _range;

    /// <summary>
    /// The rows the main view shows when it hides lines (the results' lines), or null when it shows every line.
    /// A multi-line selection shows only these rows unless <see cref="IncludeHiddenLines"/> is on.
    /// </summary>
    [ObservableProperty]
    private ILineList? _shownLines;

    /// <summary>A multi-line selection also shows the hidden lines between its ends (off by default; app-wide setting).</summary>
    [ObservableProperty]
    private bool _includeHiddenLines;

    /// <summary>True while several lines are shown: the copy button reads "Copy lines".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CopyLabel))]
    private bool _isMultiLine;

    public string CopyLabel => IsMultiLine ? "Copy lines" : "Copy line";

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private string _header = NoLineHeader;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Notice))]
    private bool _isCapped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Notice))]
    private string _cappedNotice = "";

    /// <summary>Completes when the text for the current line has been loaded (tests await it).</summary>
    public Task WhenLoaded { get; private set; } = Task.CompletedTask;

    /// <summary>Shown above the text when the line was too long to show completely.</summary>
    public string Notice => IsCapped ? CappedNotice : "";

    private static string LineCapNotice =>
        $"Showing the first {MaxShownChars.ToString("N0", CultureInfo.CurrentCulture)} characters. Copy line copies all of it.";

    private static string RangeCapNotice =>
        $"Showing the first {MaxShownLines.ToString("N0", CultureInfo.CurrentCulture)} lines or " +
        $"{MaxShownChars.ToString("N0", CultureInfo.CurrentCulture)} characters. Use Ctrl+C in the view to copy all of them.";

    /// <summary>
    /// Shows file lines <paramref name="first"/> to <paramref name="last"/> (0-based, inclusive): a selection
    /// over several lines. Setting <see cref="CurrentLine"/> goes back to one line.
    /// </summary>
    public void ShowRange(long first, long last)
    {
        if (last <= first)
        {
            var wasRange = _range is not null;
            _range = null;
            if (CurrentLine != first) CurrentLine = first;
            else if (wasRange) Load();
            return;
        }

        if (_range == (first, last)) return;
        _range = (first, last);
        Load();
    }

    /// <summary>
    /// Reads the shown text for copying: the whole current line, however long, or the shown lines of a range in
    /// full (at most <see cref="MaxShownLines"/>), one per line. Null if there is none.
    /// </summary>
    public Task<string?> ReadFullLineAsync()
    {
        var reader = Reader;
        if (reader is null) return Task.FromResult<string?>(null);
        if (_range is { } range)
        {
            var shown = IncludeHiddenLines ? null : ShownLines;
            return Task.Run(() =>
            {
                var lines = LinesIn(range, shown, out _);
                return lines.Count == 0
                    ? null
                    : string.Join(Environment.NewLine, ReadRuns(reader, lines, null).Select(p => p.Text));
            });
        }

        var line = CurrentLine;
        if (line is not { } l) return Task.FromResult<string?>(null);
        return Task.Run(() => reader.ReadLines(l, 1) is [var text] ? text : null);
    }

    partial void OnReaderChanged(ILineReader? value)
    {
        // A new document (or a reload): the old line numbers mean nothing there.
        _range = null;
        if (CurrentLine is null) Load();
        else CurrentLine = null;
    }

    partial void OnCurrentLineChanged(long? value)
    {
        _range = null;
        Load();
    }

    partial void OnShownLinesChanged(ILineList? value)
    {
        if (_range is not null) Load();
    }

    partial void OnIncludeHiddenLinesChanged(bool value)
    {
        if (_range is not null) Load();
    }

    private void Load() => WhenLoaded = _range is { } range
        ? LoadRangeAsync(range, IncludeHiddenLines ? null : ShownLines, ++_loadVersion)
        : LoadAsync(CurrentLine, ++_loadVersion);

    /// <summary>
    /// The file lines of <paramref name="range"/> to show: every one, or only those in <paramref name="shown"/>
    /// (ascending); at most <see cref="MaxShownLines"/>. <paramref name="total"/> is how many there are in all.
    /// </summary>
    private static List<long> LinesIn((long First, long Last) range, ILineList? shown, out long total)
    {
        List<long> lines = [];
        if (shown is null)
        {
            total = range.Last - range.First + 1;
            for (var line = range.First; line <= range.Last && lines.Count < MaxShownLines; line++) lines.Add(line);
            return lines;
        }

        var first = FirstRowAbove(shown, range.First - 1);
        var end = FirstRowAbove(shown, range.Last);
        total = end - first;
        for (var row = first; row < end && lines.Count < MaxShownLines; row++) lines.Add(shown[row]);
        return lines;
    }

    /// <summary>The first row of <paramref name="rows"/> (ascending) whose line is above <paramref name="line"/>.</summary>
    private static long FirstRowAbove(ILineList rows, long line)
    {
        long low = 0, high = rows.Count;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (rows[mid] <= line) low = mid + 1;
            else high = mid;
        }

        return low;
    }

    /// <summary>
    /// Reads <paramref name="lines"/> (ascending), consecutive ones in one call, each cut at
    /// <paramref name="maxChars"/> (null: in full).
    /// </summary>
    private static List<LinePrefix> ReadRuns(ILineReader reader, List<long> lines, int? maxChars)
    {
        var texts = new List<LinePrefix>(lines.Count);
        for (var i = 0; i < lines.Count;)
        {
            var run = 1;
            while (i + run < lines.Count && lines[i + run] == lines[i] + run) run++;
            var read = maxChars is { } max
                ? reader.ReadLinePrefixes(lines[i], run, max)
                : [.. reader.ReadLines(lines[i], run).Select(t => new LinePrefix(t, false))];
            texts.AddRange(read);
            if (read.Length < run) break; // the lines are no longer there
            i += run;
        }

        return texts;
    }

    private async Task LoadRangeAsync((long First, long Last) range, ILineList? shown, int version)
    {
        var reader = Reader;
        if (reader is null)
        {
            Show(NoLineHeader, "", false);
            return;
        }

        var culture = CultureInfo.CurrentCulture;
        var span = $"{(range.First + 1).ToString("N0", culture)}–{(range.Last + 1).ToString("N0", culture)}";
        try
        {
            var (text, count, capped) = await Task.Run(() =>
            {
                var lines = LinesIn(range, shown, out var total);
                var builder = new System.Text.StringBuilder();
                var capped = total > lines.Count;
                foreach (var prefix in ReadRuns(reader, lines, MaxShownChars))
                {
                    if (builder.Length > 0) builder.Append('\n');
                    var room = MaxShownChars - builder.Length;
                    if (prefix.Text.Length > room)
                    {
                        builder.Append(prefix.Text, 0, Math.Max(0, room));
                        capped = true;
                        break;
                    }

                    builder.Append(prefix.Text);
                    capped |= prefix.IsTruncated;
                }

                return (builder.ToString(), total, capped);
            });
            if (version != _loadVersion) return; // another selection was made meanwhile
            var header = $"Lines {span} ({count.ToString("N0", culture)} {(count == 1 ? "line" : "lines")})";
            Show(header, text, capped, multiLine: true);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or ArgumentOutOfRangeException)
        {
            if (version == _loadVersion) Show($"Lines {span} are no longer available", "", false, multiLine: true);
        }
    }

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

    private void Show(string header, string text, bool capped, bool multiLine = false)
    {
        Header = header;
        Text = text;
        CappedNotice = multiLine ? RangeCapNotice : LineCapNotice;
        IsCapped = capped;
        IsMultiLine = multiLine;
    }
}
