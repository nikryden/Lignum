using System.Windows;
using System.Windows.Media;
using Lignum.Core.Syntax;

namespace Lignum.App.Viewer;

/// <summary>
/// Syntax coloring (ticket 25): when <see cref="FilePath"/> has a scheme in <see cref="SyntaxHighlighters"/>, each
/// visible row's text is colored token by token and the whole-line rules are not used. Hand-set line colors still
/// win (a hand-set text color colors the whole line). Tokens are found once per loaded row set, not on every redraw.
/// </summary>
public sealed partial class LogView
{
    public static readonly DependencyProperty SyntaxHighlightersProperty = DependencyProperty.Register(
        nameof(SyntaxHighlighters), typeof(SyntaxHighlighterSet), typeof(LogView),
        new FrameworkPropertyMetadata(SyntaxHighlighterSet.None, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(LogView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    // Tokens for _tokenRows (the _rows list they were found for), by row; null entries are not found yet.
    private IReadOnlyList<DisplayRow>? _tokenRows;
    private SyntaxHighlighter? _tokensWith;
    private IReadOnlyList<SyntaxToken>?[] _tokens = [];

    /// <summary>The schemes by file type; <see cref="SyntaxHighlighterSet.None"/> for none.</summary>
    public SyntaxHighlighterSet SyntaxHighlighters
    {
        get => (SyntaxHighlighterSet)GetValue(SyntaxHighlightersProperty);
        set => SetValue(SyntaxHighlightersProperty, value);
    }

    /// <summary>The file shown, whose name picks the syntax scheme.</summary>
    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    /// <summary>The scheme for this file, or null when the whole-line rules apply.</summary>
    private SyntaxHighlighter? Syntax => (SyntaxHighlighters ?? SyntaxHighlighterSet.None).For(FilePath);

    private IReadOnlyList<SyntaxToken> TokensFor(SyntaxHighlighter syntax, long row, DisplayRow displayRow)
    {
        if (!ReferenceEquals(_tokenRows, _rows) || !ReferenceEquals(_tokensWith, syntax))
        {
            _tokenRows = _rows;
            _tokensWith = syntax;
            _tokens = new IReadOnlyList<SyntaxToken>?[_rows.Count];
        }

        var i = row - _rowsFirst;
        if (i < 0 || i >= _tokens.Length) return syntax.Tokenize(displayRow.Text);
        return _tokens[i] ??= syntax.Tokenize(displayRow.Text);
    }

    /// <summary>
    /// Colors <paramref name="text"/> (the row's tab-expanded text from display column <paramref name="firstColumn"/>)
    /// with the row's tokens, which are character ranges of the raw row text.
    /// </summary>
    private void ApplySyntax(FormattedText text, string displayed, SyntaxHighlighter syntax, long row,
                             DisplayRow displayRow, int firstColumn)
    {
        var tokens = TokensFor(syntax, row, displayRow);
        if (tokens.Count == 0) return;

        var raw = displayRow.Text;
        var hasTabs = raw.Contains('\t');
        foreach (var token in tokens)
        {
            var start = (hasTabs ? TabExpansion.IndexToColumn(raw, TabWidth, token.Start) : token.Start) - firstColumn;
            var end = (hasTabs ? TabExpansion.IndexToColumn(raw, TabWidth, token.Start + token.Length) : token.Start + token.Length) - firstColumn;
            start = Math.Max(0, start);
            end = Math.Min(displayed.Length, end);
            if (end <= start) continue;

            var style = syntax.StyleOf(token);
            if (style.Foreground is { } brush) text.SetForegroundBrush(brush, start, end - start);
            if (style.Weight != FontWeights.Normal) text.SetFontWeight(style.Weight, start, end - start);
            if (style.Style != FontStyles.Normal) text.SetFontStyle(style.Style, start, end - start);
        }
    }
}
