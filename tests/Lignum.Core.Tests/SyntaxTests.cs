using Lignum.Core.Syntax;

namespace Lignum.Core.Tests;

public sealed class SyntaxTests
{
    private static SyntaxRule Rule(string pattern, bool matchCase = true) => new() { Pattern = pattern, MatchCase = matchCase };

    private static string[] Parts(SyntaxTokenizer tokenizer, string line) =>
        tokenizer.Tokenize(line).Select(t => $"{t.Rule}:{line.Substring(t.Start, t.Length)}").ToArray();

    [Fact]
    public void The_earliest_match_wins_so_keywords_inside_strings_and_comments_stay_part_of_them()
    {
        var tokenizer = new SyntaxTokenizer([Rule("//.*"), Rule("\"[^\"]*\""), Rule(@"\b(var|return)\b")]);

        Assert.Equal(["2:var", "1:\"return x\"", "0:// var"], Parts(tokenizer, "var s = \"return x\"; // var"));
    }

    [Fact]
    public void Among_matches_at_the_same_place_the_first_rule_wins()
    {
        var tokenizer = new SyntaxTokenizer([Rule(@"\bnull\b"), Rule(@"\b\w+\b")]);

        Assert.Equal(["0:null", "1:value"], Parts(tokenizer, "null value"));
        Assert.Equal(["0:null"], Parts(new SyntaxTokenizer([Rule(@"\w+"), Rule(@"\bnull\b")]), "null"));
    }

    [Fact]
    public void Disabled_invalid_and_empty_matching_rules_are_skipped()
    {
        var tokenizer = new SyntaxTokenizer(
        [
            new SyntaxRule { Pattern = "a", IsEnabled = false },
            Rule("(unclosed"),
            Rule("x*"), // matches empty at every position
            Rule("b"),
        ]);

        Assert.Equal(["2:x", "3:b"], Parts(tokenizer, "a x b"));
        Assert.Null(SyntaxTokenizer.Validate("ok"));
        Assert.NotNull(SyntaxTokenizer.Validate("(unclosed"));
    }

    [Fact]
    public void Match_case_off_ignores_case()
    {
        var tokenizer = new SyntaxTokenizer([Rule(@"\bselect\b", matchCase: false)]);

        Assert.Equal(["0:SELECT"], Parts(tokenizer, "SELECT 1"));
        Assert.Empty(new SyntaxTokenizer([Rule(@"\bselect\b")]).Tokenize("SELECT 1"));
    }

    [Fact]
    public void A_rule_that_times_out_is_skipped_and_the_others_still_color()
    {
        var tokenizer = new SyntaxTokenizer([Rule(@"^(a+)+$"), Rule("!")]);
        var line = new string('a', 40) + "!";

        Assert.Equal(["1:!"], Parts(tokenizer, line));
    }

    [Fact]
    public void Lookbehinds_and_word_boundaries_see_the_text_before_the_position()
    {
        var tokenizer = new SyntaxTokenizer([Rule(@"(?<=\.)\w+"), Rule(@"\bid\b")]);

        Assert.Equal(["0:Name", "1:id"], Parts(tokenizer, "user.Name grid id"));
    }

    [Fact]
    public void Extension_lists_are_parsed_leniently_and_exclude_log_and_text_types()
    {
        Assert.Equal([".cs", ".csx", "dockerfile", ".h"], SyntaxFileTypes.ParseExtensions("cs; *.CSX, Dockerfile .h .cs"));
        Assert.Empty(SyntaxFileTypes.ParseExtensions(".txt; log; *.lm"));
        Assert.Equal(".cs; .csx", SyntaxFileTypes.FormatExtensions([".cs", ".csx"]));
    }

    [Fact]
    public void A_file_uses_the_scheme_for_its_name_then_its_extension_and_log_types_use_none()
    {
        var csharp = new SyntaxScheme { Name = "C#", Extensions = [".cs"] };
        var docker = new SyntaxScheme { Name = "Docker", Extensions = ["dockerfile", ".dockerfile"] };
        var sneaky = new SyntaxScheme { Name = "Text", Extensions = [".txt", ".log"] }; // hand-edited file
        SyntaxScheme[] schemes = [csharp, docker, sneaky];

        Assert.Same(csharp, SyntaxFileTypes.Find(schemes, @"C:\src\Program.CS"));
        Assert.Same(docker, SyntaxFileTypes.Find(schemes, "/repo/Dockerfile"));
        Assert.Same(docker, SyntaxFileTypes.Find(schemes, "api.dockerfile"));
        Assert.Null(SyntaxFileTypes.Find(schemes, "app.log"));
        Assert.Null(SyntaxFileTypes.Find(schemes, "notes.txt"));
        Assert.Null(SyntaxFileTypes.Find(schemes, "unknown.xyz"));
        Assert.Null(SyntaxFileTypes.Find(schemes, "README"));
    }
}
