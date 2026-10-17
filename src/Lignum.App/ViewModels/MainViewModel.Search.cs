using CommunityToolkit.Mvvm.ComponentModel;

namespace Lignum.App.ViewModels;

// Ticket 08: app-wide search settings that every tab follows.
public sealed partial class MainViewModel
{
    /// <summary>
    /// Highlight search matches in the full log even on lines the filter rules hide (on by default). Off:
    /// only lines that pass the filter are highlighted. Saved in settings.json.
    /// </summary>
    [ObservableProperty]
    private bool _highlightMatchesOutsideFilter = true;

    private AllTabsSearchViewModel? _allTabsSearch;
    private Assistant.AssistantViewModel? _assistant;

    /// <summary>Where AI tool calls are logged (the app passes its diagnostics log).</summary>
    public Serilog.ILogger DiagnosticsLog { get; init; } = Serilog.Core.Logger.None;

    private DetailPaneLayoutViewModel? _assistantPane;

    /// <summary>Where the AI panel sits (right or bottom), its size and whether it is open; saved in settings.json.</summary>
    public DetailPaneLayoutViewModel AssistantPane => _assistantPane ??= new DetailPaneLayoutViewModel(_settings,
        s => s.AssistantPane ??= new Settings.DetailPaneSettings { IsOpen = false, Width = 380 });

    /// <summary>AI access (ticket 05): AI tools on this PC use Lignum's tools over MCP.</summary>
    public Assistant.AssistantViewModel Assistant => _assistant ??=
        new Assistant.AssistantViewModel(_settings, new Assistant.AssistantTools(this), _uiContext, DiagnosticsLog, AssistantPane);

    /// <summary>Where the Find window was last shown (settings.json); null until it has been closed once.</summary>
    internal Settings.WindowBounds? FindWindowBounds
    {
        get => _settings.Value.FindWindow;
        set
        {
            _settings.Value.FindWindow = value;
            _settings.Save();
        }
    }

    /// <summary>Find in all tabs (ticket 01): one search over every open tab, shown in the shared pane.</summary>
    public AllTabsSearchViewModel AllTabsSearch => _allTabsSearch ??= new AllTabsSearchViewModel(_uiContext,
        () => [.. Tabs], (tab, line) =>
        {
            SelectedTab = tab;
            if (line >= 0) tab.Navigation.GoToLine(line);
        }, async (path, line) =>
        {
            await OpenFileAsync(path); // find in files: open (or switch to) the file, then go to the line
            if (FindTab(System.IO.Path.GetFullPath(path)) is not { } tab) return;
            SelectedTab = tab;
            if (line >= 0) tab.Navigation.GoToLine(line);
        });

    partial void OnHighlightMatchesOutsideFilterChanged(bool value)
    {
        foreach (var tab in Tabs) tab.Search.HighlightOutsideFilter = value;
        if (_settings.Value.HighlightMatchesOutsideFilter == value) return;
        _settings.Value.HighlightMatchesOutsideFilter = value;
        _settings.Save();
    }
}
