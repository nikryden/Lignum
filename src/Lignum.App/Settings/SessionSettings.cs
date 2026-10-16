using Lignum.Core;

namespace Lignum.App.Settings;

/// <summary>
/// The open tabs saved on exit and reopened on start (the "session" in settings.json). Only what is
/// needed to put the user back where they were: never the line index, which is rebuilt in the background.
/// </summary>
public sealed class SessionSettings
{
    /// <summary>The open tabs, in tab-strip order.</summary>
    public List<SessionTab> Tabs { get; set; } = [];

    /// <summary>Index into <see cref="Tabs"/> of the tab that was selected; -1 for none.</summary>
    public int SelectedIndex { get; set; } = -1;
}

/// <summary>One saved tab. A class with named fields so later tickets can add per-tab state.</summary>
public sealed class SessionTab
{
    public string Path { get; set; } = "";

    /// <summary>The 0-based file line at the top of the viewer.</summary>
    public long FirstLine { get; set; }

    public List<QueryRule> FilterRules { get; set; } = [];
}
