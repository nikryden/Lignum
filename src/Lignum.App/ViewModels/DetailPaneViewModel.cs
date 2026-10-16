using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Settings;
using Lignum.App.Viewer;

namespace Lignum.App.ViewModels;

/// <summary>
/// The detail pane: the current line in full, word-wrapped and read-only, plus the pane's layout (open or
/// collapsed, docked right or bottom, its size), which is saved in settings.json.
/// <para>
/// <see cref="CurrentLine"/> is the hook for the selection (ticket 08) and the caret (ticket 05): set it to
/// the 0-based file line to show. Text is read off the UI thread through <see cref="Reader"/>.
/// </para>
/// </summary>
public sealed partial class DetailPaneViewModel : ObservableObject
{
    /// <summary>Most characters put in the text box; a WPF TextBox wrapping many megabytes becomes unusable.</summary>
    /// (1M chars took ~1.2 s of UI-thread layout in a smoke test; 250K keeps it well under that.)
    public const int MaxShownChars = 250_000;

    /// <summary>Smallest width or height the pane can be dragged to.</summary>
    public const double MinPaneSize = 80;

    private readonly SettingsFile<AppSettings>? _settings;
    private readonly DetailPaneSettings _layout;
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

    private const string NoLineHeader = "No line selected";

    /// <param name="settings">The app's one settings.json; null keeps the layout in memory only.</param>
    public DetailPaneViewModel(SettingsFile<AppSettings>? settings = null)
    {
        _settings = settings;
        _layout = settings is null ? new DetailPaneSettings() : settings.Value.DetailPane ??= new DetailPaneSettings();
        _layout.Width = ClampSize(_layout.Width, DetailPaneSettings.DefaultWidth);
        _layout.Height = ClampSize(_layout.Height, DetailPaneSettings.DefaultHeight);
        if (!Enum.IsDefined(_layout.Dock)) _layout.Dock = DetailPaneDock.Right;
    }

    /// <summary>Completes when the text for the current line has been loaded (tests await it).</summary>
    public Task WhenLoaded { get; private set; } = Task.CompletedTask;

    /// <summary>Shown above the text when the line was too long to show completely.</summary>
    public string Notice => IsCapped
        ? $"Showing the first {MaxShownChars.ToString("N0", CultureInfo.CurrentCulture)} characters. Copy line copies all of it."
        : "";

    public bool IsOpen
    {
        get => _layout.IsOpen;
        set => SetLayout(_layout.IsOpen, value, v => _layout.IsOpen = v);
    }

    public DetailPaneDock Dock
    {
        get => _layout.Dock;
        set => SetLayout(_layout.Dock, value, v => _layout.Dock = v);
    }

    /// <summary>Width when docked right.</summary>
    public double PaneWidth
    {
        get => _layout.Width;
        set => SetLayout(_layout.Width, ClampSize(value, DetailPaneSettings.DefaultWidth), v => _layout.Width = v);
    }

    /// <summary>Height when docked at the bottom.</summary>
    public double PaneHeight
    {
        get => _layout.Height;
        set => SetLayout(_layout.Height, ClampSize(value, DetailPaneSettings.DefaultHeight), v => _layout.Height = v);
    }

    /// <summary>"Show full line": opens the pane on <paramref name="line"/> (0-based).</summary>
    public void ShowFullLine(long line)
    {
        CurrentLine = line;
        IsOpen = true;
    }

    /// <summary>Reads the whole current line (for copying), however long. Null if there is none.</summary>
    public Task<string?> ReadFullLineAsync()
    {
        var reader = Reader;
        var line = CurrentLine;
        if (reader is null || line is not { } l) return Task.FromResult<string?>(null);
        return Task.Run(() => reader.ReadLines(l, 1) is [var text] ? text : null);
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void DockRight() => Dock = DetailPaneDock.Right;

    [RelayCommand]
    private void DockBottom() => Dock = DetailPaneDock.Bottom;

    partial void OnReaderChanged(ILineReader? value)
    {
        // A new document: the old line number means nothing there.
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

        string header = $"Line {(l + 1).ToString("N0", CultureInfo.CurrentCulture)}";
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

    private void SetLayout<T>(T current, T value, Action<T> store, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        store(value);
        OnPropertyChanged(name);
        _settings?.Save();
    }

    private static double ClampSize(double value, double fallback) =>
        double.IsFinite(value) ? Math.Max(MinPaneSize, value) : fallback;
}
