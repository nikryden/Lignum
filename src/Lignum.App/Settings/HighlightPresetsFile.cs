using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lignum.App.Settings;

/// <summary>
/// The contents of <c>highlight-presets.json</c>: named sets of line coloring rules and which one is active.
/// <see cref="Presets"/> is null on first run; <see cref="ViewModels.HighlightingViewModel"/> then creates the
/// default preset.
/// </summary>
public sealed class HighlightPresetsFile
{
    public const string FileName = "highlight-presets.json";

    public List<HighlightPresetData>? Presets { get; set; }

    /// <summary>Name of the active preset.</summary>
    public string? ActivePreset { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class HighlightPresetData
{
    public string Name { get; set; } = "";

    /// <summary>In priority order: the first enabled rule that matches a line colors it.</summary>
    public List<HighlightRuleData> Rules { get; set; } = [];
}

/// <summary>One whole-line coloring rule. Colors are <c>#RRGGBB</c> (or <c>#AARRGGBB</c>), null for none.</summary>
public sealed class HighlightRuleData
{
    public string Pattern { get; set; } = "";

    public bool IsRegex { get; set; }

    public bool MatchCase { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Colors used with the light theme.</summary>
    public HighlightColors Light { get; set; } = new();

    /// <summary>Colors used with the dark theme.</summary>
    public HighlightColors Dark { get; set; } = new();

    public HighlightRuleData Clone() => new()
    {
        Pattern = Pattern,
        IsRegex = IsRegex,
        MatchCase = MatchCase,
        IsEnabled = IsEnabled,
        Light = new HighlightColors { Foreground = Light.Foreground, Background = Light.Background },
        Dark = new HighlightColors { Foreground = Dark.Foreground, Background = Dark.Background },
    };
}

public sealed class HighlightColors
{
    public string? Foreground { get; set; }

    public string? Background { get; set; }
}
