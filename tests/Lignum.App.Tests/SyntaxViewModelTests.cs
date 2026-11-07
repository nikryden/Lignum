using System.Windows.Media;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Lignum.Core.Syntax;
using Serilog.Core;

namespace Lignum.App.Tests;

public sealed class SyntaxViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SettingsStore.Flush();
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private SyntaxViewModel Create(AppTheme theme = AppTheme.Light) => UiContext.None(() =>
        new SyntaxViewModel(new SettingsStore(_dir, Logger.None).Open<SyntaxSchemesFile>(SyntaxSchemesFile.FileName))
        {
            Theme = theme,
        });

    private static Color ColorOf(SyntaxViewModel vm, string path, string line, int token = 0)
    {
        var highlighter = vm.Highlighters.For(path)!;
        return ((SolidColorBrush)highlighter.StyleOf(highlighter.Tokenize(line)[token]).Foreground!).Color;
    }

    [Fact]
    public void On_first_run_source_files_get_the_built_in_schemes_and_log_files_get_none()
    {
        var vm = Create();

        Assert.Equal(BuiltInSyntaxSchemes.Create().Count, vm.Schemes.Count);
        Assert.Equal("C#", vm.Highlighters.For(@"C:\src\Program.cs")!.Name);
        Assert.Equal("JSON", vm.Highlighters.For("appsettings.json")!.Name);
        Assert.Null(vm.Highlighters.For("app.log"));
        Assert.Null(vm.Highlighters.For("notes.txt"));
        Assert.Null(vm.Highlighters.For("support.lm"));
        Assert.Null(vm.Highlighters.For("data.unknown"));
    }

    [Fact]
    public void Each_theme_has_its_own_colors_and_switching_replaces_the_highlighters()
    {
        var vm = Create(AppTheme.Light);
        var light = ColorOf(vm, "a.cs", "// note");
        var before = vm.Highlighters;

        vm.Theme = AppTheme.Dark;

        Assert.NotSame(before, vm.Highlighters);
        Assert.NotEqual(light, ColorOf(vm, "a.cs", "// note"));
    }

    [Fact]
    public void A_new_scheme_with_its_own_types_and_rules_is_used_and_saved()
    {
        var vm = Create();
        vm.AddSchemeCommand.Execute(null);
        var scheme = vm.SelectedScheme!;
        scheme.Name = "Acme config";
        scheme.ExtensionsText = "acme; *.ACM, .log"; // .log is not allowed
        scheme.AddRuleCommand.Execute(null);
        scheme.Rules[0].Pattern = "^@\\w+";
        scheme.Rules[0].LightColor = "#112233";

        Assert.Equal(".acme; .acm", scheme.ExtensionsText);
        Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), ColorOf(vm, "x.acm", "@section"));

        var restarted = Create();
        Assert.Equal("Acme config", restarted.Highlighters.For("y.ACME")!.Name);
        Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), ColorOf(restarted, "y.acme", "@section"));
    }

    [Fact]
    public void Rule_order_decides_ties_and_moving_a_rule_changes_the_color()
    {
        var vm = Create();
        vm.AddSchemeCommand.Execute(null);
        var scheme = vm.SelectedScheme!;
        scheme.ExtensionsText = ".tie";
        scheme.AddRuleCommand.Execute(null);
        scheme.AddRuleCommand.Execute(null);
        (scheme.Rules[0].Pattern, scheme.Rules[0].LightColor) = ("ab", "#FF0000");
        (scheme.Rules[1].Pattern, scheme.Rules[1].LightColor) = ("abc", "#0000FF");
        Assert.Equal(Colors.Red, ColorOf(vm, "a.tie", "abc"));

        scheme.MoveRuleDownCommand.Execute(scheme.Rules[0]);

        Assert.Equal(Colors.Blue, ColorOf(vm, "a.tie", "abc"));
    }

    [Fact]
    public void An_invalid_regex_shows_a_problem_and_colors_nothing()
    {
        var vm = Create();
        var rule = vm.Schemes.First(s => s.Name == "C#").Rules[0];

        rule.Pattern = "(unclosed";

        Assert.NotNull(rule.Error);
        Assert.Empty(vm.Highlighters.For("a.cs")!.Tokenize("(unclosed").Where(t => t.Rule == 0));
    }

    [Fact]
    public void Exported_schemes_import_again_and_name_clashes_replace_or_keep_both()
    {
        var vm = Create();
        var csharp = vm.Schemes.First(s => s.Name == "C#");
        csharp.Rules[0].LightColor = "#ABCDEF";
        var path = Path.Combine(_dir, "csharp.json");
        Directory.CreateDirectory(_dir);
        vm.Export(path, [csharp]);
        csharp.Rules[0].LightColor = "#000000";

        var read = SyntaxViewModel.ReadSchemes(path);
        Assert.Equal(["C#"], read.Select(s => s.Name));
        Assert.Equal(["C#"], vm.Conflicts(read));

        vm.Import(read, replace: false);
        Assert.Contains(vm.Schemes, s => s.Name == "C# (2)");
        Assert.Equal("#000000", vm.Schemes.First(s => s.Name == "C#").Rules[0].LightColor);

        vm.Import(read, replace: true);
        Assert.Equal("#ABCDEF", vm.Schemes.First(s => s.Name == "C#").Rules[0].LightColor);
        Assert.Single(vm.Schemes, s => s.Name == "C#");

        var all = Path.Combine(_dir, "all.json");
        vm.Export(all);
        Assert.Equal(vm.Schemes.Count, SyntaxViewModel.ReadSchemes(all).Count);
    }

    [Fact]
    public void A_file_that_is_not_a_schemes_file_is_refused()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "empty.json");
        File.WriteAllText(path, "{ \"schemes\": [] }");

        Assert.Throws<InvalidDataException>(() => SyntaxViewModel.ReadSchemes(path));
    }

    [Fact]
    public void Factory_defaults_bring_back_the_built_in_schemes()
    {
        var vm = Create();
        vm.AddSchemeCommand.Execute(null);
        vm.Schemes.First(s => s.Name == "C#").Rules.Clear();
        vm.SelectedScheme = vm.Schemes.First(s => s.Name == "JSON");
        vm.DeleteSchemeCommand.Execute(null);

        vm.ResetToFactoryDefaults();

        var builtIn = BuiltInSyntaxSchemes.Create();
        Assert.Equal(builtIn.Select(s => s.Name), vm.Schemes.Select(s => s.Name));
        Assert.Equal(builtIn[0].Rules.Count, vm.Schemes[0].Rules.Count);
        Assert.Equal("JSON", Create().Highlighters.For("a.json")!.Name);
    }

    [Fact]
    public void Duplicating_a_scheme_copies_its_rules_but_not_its_file_types()
    {
        var vm = Create();
        vm.SelectedScheme = vm.Schemes.First(s => s.Name == "C#");

        vm.DuplicateSchemeCommand.Execute(null);

        Assert.Equal("C# copy", vm.SelectedScheme!.Name);
        Assert.Equal("", vm.SelectedScheme.ExtensionsText);
        Assert.Equal(vm.Schemes.First(s => s.Name == "C#").Rules.Count, vm.SelectedScheme.Rules.Count);
        Assert.Equal("C#", vm.Highlighters.For("a.cs")!.Name);
    }
}
