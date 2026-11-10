using Lignum.Core;

namespace Lignum.App.Settings;

/// <summary>
/// The open tabs saved on exit and reopened on start (the "session" in settings.json). Only what is
/// needed to put the user back where they were: never the line index, which is rebuilt in the background.
/// </summary>
public sealed class SessionSettings
{
    /// <summary>The open tabs: the main window's in tab-strip order, then each floating window's.</summary>
    public List<SessionTab> Tabs { get; set; } = [];

    /// <summary>Index into <see cref="Tabs"/> of the tab shown in the main window; -1 for none.</summary>
    public int SelectedIndex { get; set; } = -1;

    /// <summary>The floating windows (ticket 28); <see cref="SessionTab.Window"/> 1 is the first. Missing in older files.</summary>
    public List<SessionWindow> Windows { get; set; } = [];
}

/// <summary>A floating window of tabs (ticket 28): where it was, and which of its tabs it showed.</summary>
public sealed class SessionWindow
{
    /// <summary>Its normal (not maximized) position and size; null: open it under the mouse.</summary>
    public WindowBounds? Bounds { get; set; }

    public bool Maximized { get; set; }

    /// <summary>Index into <see cref="SessionSettings.Tabs"/> of the tab it showed; -1 for none.</summary>
    public int SelectedIndex { get; set; } = -1;
}

/// <summary>One saved tab. A class with named fields so later tickets can add per-tab state.</summary>
public sealed class SessionTab
{
    public string Path { get; set; } = "";

    /// <summary>The window it was in (ticket 28): 0 the main window, n the n-th of <see cref="SessionSettings.Windows"/>.</summary>
    public int Window { get; set; }

    /// <summary>The 0-based file line at the top of the viewer.</summary>
    public long FirstLine { get; set; }

    public List<QueryRule> FilterRules { get; set; } = [];

    /// <summary>The active search (ticket 08), or null; with its options.</summary>
    public string? SearchText { get; set; }

    public bool SearchIsRegex { get; set; }

    public bool SearchMatchCase { get; set; }

    public bool SearchNonBacktracking { get; set; }

    public bool SearchInsideFilter { get; set; } = true;

    public bool HideNonMatchingLines { get; set; }
}
