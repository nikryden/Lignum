using System.Text.RegularExpressions;
using System.Windows.Media;
using Lignum.App.Settings;
using Lignum.App.ViewModels;
using Lignum.Core.Searching;

namespace Lignum.App.Viewer;

/// <summary>Colors for a whole line. Brushes are frozen, so they can be shared and drawn cheaply.</summary>
public sealed record LineStyle(SolidColorBrush? Foreground, SolidColorBrush? Background);

/// <summary>
/// Compiled, immutable line coloring rules for one theme. <see cref="Resolve"/> returns the style of the
/// first enabled rule that matches the line (first match wins), or null. Disabled rules, empty patterns and
/// invalid regexes are skipped. Regexes run with a short timeout; a timeout counts as no match.
/// </summary>
public sealed class LineHighlighter
{
    /// <summary>Regex timeout per rule and line. Short: resolution runs on the UI thread while drawing.</summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    public static readonly LineHighlighter None = new([]);

    private readonly CompiledRule[] _rules;

    private LineHighlighter(CompiledRule[] rules) => _rules = rules;

    public bool IsEmpty => _rules.Length == 0;

    public static LineHighlighter Build(IEnumerable<HighlightRuleData> rules, AppTheme theme)
    {
        var compiled = new List<CompiledRule>();
        foreach (var rule in rules)
        {
            if (!rule.IsEnabled || string.IsNullOrEmpty(rule.Pattern)) continue;
            var colors = theme == AppTheme.Dark ? rule.Dark : rule.Light;
            var style = new LineStyle(ToBrush(colors.Foreground), ToBrush(colors.Background));
            TextPattern pattern;
            try
            {
                pattern = TextPattern.Create(rule.Pattern, rule.IsRegex, rule.MatchCase, RegexTimeout);
            }
            catch (ArgumentException)
            {
                continue; // invalid regex
            }

            compiled.Add(new CompiledRule(pattern, style));
        }

        return compiled.Count == 0 ? None : new LineHighlighter([.. compiled]);
    }

    /// <summary>The style of the first matching rule, or null when no rule matches.</summary>
    public LineStyle? Resolve(string text)
    {
        foreach (var rule in _rules)
        {
            if (rule.IsMatch(text)) return rule.Style;
        }

        return null;
    }

    /// <summary>Parses <c>#RRGGBB</c> / <c>#AARRGGBB</c> (or a named color); null when empty or invalid.</summary>
    public static Color? ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return ColorConverter.ConvertFromString(text.Trim()) as Color?;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    internal static SolidColorBrush? ToBrush(string? text)
    {
        if (ParseColor(text) is not { } color) return null;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private sealed record CompiledRule(TextPattern Pattern, LineStyle Style)
    {
        public bool IsMatch(string line)
        {
            try
            {
                return Pattern.IsMatch(line);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
