using System.Windows;
using System.Windows.Media;
using Lignum.App.ViewModels;
using Lignum.Core.Syntax;

namespace Lignum.App.Viewer;

/// <summary>How one token rule is drawn with the current theme: a brush (null = the line's text color), weight, style.</summary>
public sealed record TokenStyle(Brush? Foreground, FontWeight Weight, FontStyle Style);

/// <summary>One <see cref="SyntaxScheme"/> compiled for a theme (ticket 25). Immutable; shared by all viewers.</summary>
public sealed class SyntaxHighlighter
{
    private readonly SyntaxTokenizer _tokenizer;
    private readonly TokenStyle[] _styles;

    public SyntaxHighlighter(SyntaxScheme scheme, AppTheme theme)
    {
        Name = scheme.Name;
        _tokenizer = new SyntaxTokenizer(scheme.Rules);
        _styles = scheme.Rules.Select(r => new TokenStyle(
            LineHighlighter.ToBrush(theme == AppTheme.Dark ? r.DarkColor : r.LightColor),
            r.Bold ? FontWeights.Bold : FontWeights.Normal,
            r.Italic ? FontStyles.Italic : FontStyles.Normal)).ToArray();
    }

    public string Name { get; }

    public IReadOnlyList<SyntaxToken> Tokenize(string line) => _tokenizer.Tokenize(line);

    public TokenStyle StyleOf(SyntaxToken token) => _styles[token.Rule];
}

/// <summary>
/// All schemes compiled for one theme, looked up by file path. Replaced (never changed) when a scheme or the theme
/// changes, so a viewer can tell by reference that it must recolor. Schemes compile on first use.
/// </summary>
public sealed class SyntaxHighlighterSet
{
    public static readonly SyntaxHighlighterSet None = new([], AppTheme.Light);

    private readonly IReadOnlyList<SyntaxScheme> _schemes;
    private readonly AppTheme _theme;
    private readonly Dictionary<SyntaxScheme, SyntaxHighlighter> _compiled = [];

    public SyntaxHighlighterSet(IReadOnlyList<SyntaxScheme> schemes, AppTheme theme)
    {
        _schemes = schemes;
        _theme = theme;
    }

    /// <summary>The highlighter for <paramref name="path"/>, or null when it keeps the whole-line rules.</summary>
    public SyntaxHighlighter? For(string? path)
    {
        if (SyntaxFileTypes.Find(_schemes, path) is not { } scheme) return null;
        lock (_compiled)
        {
            if (!_compiled.TryGetValue(scheme, out var highlighter))
                _compiled[scheme] = highlighter = new SyntaxHighlighter(scheme, _theme);
            return highlighter;
        }
    }
}
