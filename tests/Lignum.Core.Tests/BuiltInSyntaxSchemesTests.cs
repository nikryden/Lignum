using System.Diagnostics;
using Lignum.Core.Syntax;

namespace Lignum.Core.Tests;

public sealed class BuiltInSyntaxSchemesTests
{
    private static readonly List<SyntaxScheme> Schemes = BuiltInSyntaxSchemes.Create();

    private static SyntaxScheme For(string path) =>
        SyntaxFileTypes.Find(Schemes, path) ?? throw new InvalidOperationException($"No scheme for {path}");

    /// <summary>"RuleName:text" for each token of <paramref name="line"/>.</summary>
    private static string[] Tokens(string path, string line)
    {
        var scheme = For(path);
        return new SyntaxTokenizer(scheme.Rules).Tokenize(line)
            .Select(t => $"{scheme.Rules[t.Rule].Name}:{line.Substring(t.Start, t.Length)}").ToArray();
    }

    [Fact]
    public void Every_scheme_is_usable()
    {
        Assert.True(Schemes.Count >= 40, $"{Schemes.Count} schemes");
        Assert.Equal(Schemes.Count, Schemes.Select(s => s.Name).Distinct().Count());
        foreach (var scheme in Schemes)
        {
            Assert.NotEmpty(scheme.Extensions);
            Assert.DoesNotContain(scheme.Extensions, SyntaxFileTypes.Excluded.Contains);
            foreach (var rule in scheme.Rules)
            {
                Assert.True(SyntaxTokenizer.Validate(rule.Pattern) is null, $"{scheme.Name} / {rule.Name}: {SyntaxTokenizer.Validate(rule.Pattern)}");
                Assert.False(string.IsNullOrEmpty(rule.LightColor) || string.IsNullOrEmpty(rule.DarkColor), $"{scheme.Name} / {rule.Name}");
            }
        }
    }

    [Fact]
    public void Each_extension_belongs_to_one_scheme()
    {
        var duplicates = Schemes.SelectMany(s => s.Extensions.Select(e => (e, s.Name)))
            .GroupBy(x => x.e).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Name))}");

        Assert.Empty(duplicates);
    }

    [Fact]
    public void No_scheme_is_slow_on_long_or_awkward_lines()
    {
        string[] lines =
        [
            new string('a', 10_000),
            string.Concat(Enumerable.Repeat("\"x\\\" '/* <a b=\"c\"> #{$v} ", 400)),
            new string('"', 5_000) + new string('\'', 5_000),
            string.Concat(Enumerable.Repeat("  - key: value # c ", 500)),
        ];
        // Catches catastrophic patterns (each search can burn up to its 100 ms match timeout, many times per
        // line), not small slowdowns. The worst honest case is CSS on the second line, about 80 ms on a
        // desktop: the tokenizer searches the block comment again after each string, and with no "*/" left
        // each search runs to the end of the line. Shared CI runners were ~20x slower, so: up to three tries
        // (a slow one is retried, against scheduling noise) and a generous budget.
        foreach (var scheme in Schemes)
        {
            var tokenizer = new SyntaxTokenizer(scheme.Rules);
            var best = long.MaxValue;
            for (var run = 0; run < 3 && best >= 3_000; run++)
            {
                var watch = Stopwatch.StartNew();
                foreach (var line in lines) tokenizer.Tokenize(line);
                best = Math.Min(best, watch.ElapsedMilliseconds);
            }
            Assert.True(best < 3_000, $"{scheme.Name}: {best} ms (best of 3 tries)");
        }
    }

    [Fact]
    public void Common_files_are_colored_as_an_editor_would()
    {
        Assert.Equal(["Keyword:public", "Built-in type:string", "String:\"a // b\"", "Comment:// note"],
            Tokens("Program.cs", "public string Name => \"a // b\"; // note"));
        Assert.Equal(["Type:List", "Built-in type:int", "Keyword:new", "Type:List", "Built-in type:int", "Type:Console", "Function:WriteLine"],
            Tokens("Program.cs", "List<int> ids = new List<int>(); Console.WriteLine(ids);"));
        Assert.Equal(["Key:\"name\"", "String:\"x\"", "Key:\"n\"", "Number:42", "Key:\"ok\"", "Constant:true"],
            Tokens("appsettings.json", "{ \"name\": \"x\", \"n\": 42, \"ok\": true }"));
        Assert.Equal(["Tag:<Button", "Attribute:Content", "Value:\"OK\"", "Tag:/>"],
            Tokens("MainWindow.xaml", "<Button Content=\"OK\" />"));
        Assert.Equal(["Control keyword:if", "Variable:$x", "Operator:-eq", "Number:1", "Cmdlet:Write-Host", "String:'hi'", "Comment:# done"],
            Tokens("build.ps1", "if ($x -eq 1) { Write-Host 'hi' } # done"));
        Assert.Equal(["Keyword:def", "Function:run", "Comment:# todo"], Tokens("tool.py", "def run(): # todo"));
        Assert.Equal(["Keyword:select", "Keyword:from", "Keyword:where", "String:'a'"], Tokens("q.sql", "select * from t where c = 'a'"));
        Assert.Equal(["Instruction:FROM", "Stage:AS build"], Tokens("Dockerfile", "FROM sdk:10 AS build"));
        Assert.Equal(["Added:+new line"], Tokens("fix.patch", "+new line"));
        Assert.Equal(["File header:--- a/x.cs"], Tokens("fix.patch", "--- a/x.cs"));
    }
}
