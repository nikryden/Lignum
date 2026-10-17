namespace Lignum.App.Settings;

/// <summary>Where the detail pane sits next to the main view.</summary>
public enum DetailPaneDock
{
    Right,
    Bottom,
}

/// <summary>The detail pane layout in <c>settings.json</c> (<c>"detailPane": { ... }</c>).</summary>
public sealed class DetailPaneSettings
{
    public const double DefaultWidth = 400;
    public const double DefaultHeight = 200;

    public bool IsOpen { get; set; } = true;

    public DetailPaneDock Dock { get; set; } = DetailPaneDock.Right;

    /// <summary>Width when docked right, in device-independent pixels.</summary>
    public double Width { get; set; } = DefaultWidth;

    /// <summary>Height when docked at the bottom, in device-independent pixels.</summary>
    public double Height { get; set; } = DefaultHeight;

    /// <summary>
    /// A selection over several lines also shows the lines the main view hides between its ends (off: only the
    /// selected rows).
    /// </summary>
    public bool IncludeHiddenLines { get; set; }
}
