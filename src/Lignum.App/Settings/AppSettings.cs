using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lignum.App.Settings;

/// <summary>Which theme the user picked. <see cref="System"/> follows the Windows app mode.</summary>
public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

/// <summary>
/// The contents of <c>settings.json</c>. Add a setting by adding a property with its default value
/// (camelCase in the file). Missing properties keep their defaults, so old files keep loading;
/// properties this build doesn't know are kept in <see cref="Extra"/> and written back on save.
/// Values are not validated here: the view model that owns a setting clamps it.
/// </summary>
public sealed class AppSettings
{
    public const string FileName = "settings.json";

    public const string DefaultFontFamily = "Cascadia Mono, Consolas";
    public const double DefaultFontSize = 13;
    public const int DefaultZoom = 100;
    public const int DefaultTabWidth = 4;

    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    public string FontFamily { get; set; } = DefaultFontFamily;

    /// <summary>Base font size in points, before <see cref="Zoom"/>.</summary>
    public double FontSize { get; set; } = DefaultFontSize;

    /// <summary>Ctrl+wheel zoom in percent.</summary>
    public int Zoom { get; set; } = DefaultZoom;

    public int TabWidth { get; set; } = DefaultTabWidth;

    public DetailPaneSettings DetailPane { get; set; } = new();

    /// <summary>Full paths, most recent first (File &gt; Open recent). Capped by MainViewModel.</summary>
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>Find box history, most recent first. Capped by SearchHistory.</summary>
    public List<string> SearchHistory { get; set; } = [];

    /// <summary>Filter rule text history, most recent first. Capped by SearchHistory.</summary>
    public List<string> FilterHistory { get; set; } = [];

    /// <summary>The tabs open when Lignum last exited, restored on start (ticket 18).</summary>
    public SessionSettings Session { get; set; } = new();

    /// <summary>The built-in assistant (ticket 05).</summary>
    public AssistantSettings Assistant { get; set; } = new();

    /// <summary>Where the Find window was last shown; null: centred on the main window.</summary>
    public WindowBounds? FindWindow { get; set; }

    /// <summary>Highlight search matches on lines the filter rules hide too (ticket 08).</summary>
    public bool HighlightMatchesOutsideFilter { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A window's position and size, in device-independent pixels.</summary>
public sealed class WindowBounds
{
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}

/// <summary>The built-in assistant (ticket 05). Off until the user turns it on.</summary>
public sealed class AssistantSettings
{
    public bool Enabled { get; set; }

    /// <summary>The Claude model; null uses the default.</summary>
    public string? Model { get; set; }

    /// <summary>The API key, encrypted with DPAPI for the current Windows user; null uses ANTHROPIC_API_KEY.</summary>
    public string? ProtectedApiKey { get; set; }
}
