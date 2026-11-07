namespace Lignum.Core.Syntax;

/// <summary>
/// A syntax coloring scheme (ticket 25): the file types it is for and ordered token rules. Colors are
/// <c>#RRGGBB</c> text (null for the default text color) so this stays free of WPF; the app turns them into brushes.
/// </summary>
public sealed class SyntaxScheme
{
    public string Name { get; set; } = "";

    /// <summary>
    /// Lowercase extensions with the dot (<c>.cs</c>) and whole file names without one (<c>dockerfile</c>).
    /// See <see cref="SyntaxFileTypes.ParseExtensions"/>.
    /// </summary>
    public List<string> Extensions { get; set; } = [];

    /// <summary>In priority order: at each position the earliest match wins, ties go to the first rule.</summary>
    public List<SyntaxRule> Rules { get; set; } = [];

    public SyntaxScheme Clone() => new()
    {
        Name = Name,
        Extensions = [.. Extensions],
        Rules = Rules.Select(r => r.Clone()).ToList(),
    };
}

/// <summary>One token kind of a <see cref="SyntaxScheme"/>: a regex and how its matches are drawn.</summary>
public sealed class SyntaxRule
{
    /// <summary>What the rule colors (Comment, String, Keyword…), for the editor only.</summary>
    public string Name { get; set; } = "";

    public string Pattern { get; set; } = "";

    public bool MatchCase { get; set; } = true;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Text color with the light theme, or null for the theme's text color.</summary>
    public string? LightColor { get; set; }

    /// <summary>Text color with the dark theme, or null for the theme's text color.</summary>
    public string? DarkColor { get; set; }

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public SyntaxRule Clone() => new()
    {
        Name = Name,
        Pattern = Pattern,
        MatchCase = MatchCase,
        IsEnabled = IsEnabled,
        LightColor = LightColor,
        DarkColor = DarkColor,
        Bold = Bold,
        Italic = Italic,
    };
}
