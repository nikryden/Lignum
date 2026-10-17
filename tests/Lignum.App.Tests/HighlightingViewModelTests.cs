using System.Windows.Media;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

public sealed class HighlightingViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private HighlightingViewModel Create(AppTheme theme = AppTheme.Light) => UiContext.None(() =>
        new HighlightingViewModel(
            new SettingsStore(_dir, Logger.None).Open<HighlightPresetsFile>(HighlightPresetsFile.FileName))
        {
            Theme = theme,
        });

    [Fact]
    public void On_first_run_the_default_preset_colors_error_warn_and_info_lines()
    {
        var vm = Create();

        Assert.Equal(HighlightingViewModel.DefaultPresetName, vm.ActivePreset!.Name);
        var error = vm.Highlighter.Resolve("2026-10-16 12:00:01 ERROR Connection lost");
        var warn = vm.Highlighter.Resolve("2026-10-16 12:00:01 WARN Slow response");
        var info = vm.Highlighter.Resolve("2026-10-16 12:00:01 INFO Started");
        Assert.NotNull(error);
        Assert.NotNull(warn);
        Assert.NotNull(info);
        Assert.NotEqual(error.Foreground!.Color, warn.Foreground!.Color);
        Assert.NotEqual(warn.Foreground.Color, info.Foreground!.Color);
        Assert.Null(vm.Highlighter.Resolve("2026-10-16 12:00:01 DEBUG nothing to see"));
    }

    [Fact]
    public void The_default_preset_has_its_own_colors_for_each_theme()
    {
        var light = Create(AppTheme.Light).Highlighter.Resolve("ERROR boom")!;
        var dark = Create(AppTheme.Dark).Highlighter.Resolve("ERROR boom")!;

        Assert.Equal(Color.FromRgb(0xA8, 0x00, 0x00), light.Foreground!.Color);
        Assert.Equal(Color.FromRgb(0xFF, 0x9A, 0x9A), dark.Foreground!.Color);
    }

    [Fact]
    public void Switching_the_theme_recolors_with_that_themes_colors()
    {
        var vm = Create(AppTheme.Light);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Theme = AppTheme.Dark;

        Assert.Contains(nameof(HighlightingViewModel.Highlighter), changed);
        Assert.Equal(Color.FromRgb(0xFF, 0x9A, 0x9A), vm.Highlighter.Resolve("ERROR boom")!.Foreground!.Color);
    }

    [Fact]
    public void When_rules_overlap_the_first_in_the_list_wins_and_reordering_changes_the_winner()
    {
        var vm = Create();
        vm.Rules.Clear();
        var timeout = vm.AddRule("timeout");
        var db = vm.AddRule("database"); // added rules go on top

        var line = "database timeout after 30 s";
        Assert.Same(db, vm.Rules[0]);
        Assert.Equal(Background(db), vm.Highlighter.Resolve(line)!.Background!.Color);

        vm.MoveRuleDownCommand.Execute(db);

        Assert.Same(timeout, vm.Rules[0]);
        Assert.Equal(Background(timeout), vm.Highlighter.Resolve(line)!.Background!.Color);
        Assert.Equal(Background(db), vm.Highlighter.Resolve("database only")!.Background!.Color);
    }

    [Fact]
    public void Disabled_rules_are_skipped()
    {
        var vm = Create();
        vm.Rules.Clear();
        var second = vm.AddRule("timeout");
        var first = vm.AddRule("database");

        first.IsEnabled = false;

        Assert.Equal(Background(second), vm.Highlighter.Resolve("database timeout")!.Background!.Color);
        Assert.Null(vm.Highlighter.Resolve("database ok"));
    }

    [Fact]
    public void Text_rules_ignore_case_unless_match_case_is_set()
    {
        var vm = Create();
        vm.Rules.Clear();
        var rule = vm.AddRule("Timeout");

        Assert.NotNull(vm.Highlighter.Resolve("a TIMEOUT happened"));
        rule.MatchCase = true;
        Assert.Null(vm.Highlighter.Resolve("a TIMEOUT happened"));
        Assert.NotNull(vm.Highlighter.Resolve("a Timeout happened"));
    }

    [Fact]
    public void An_invalid_regex_rule_reports_an_error_and_colors_nothing()
    {
        var vm = Create();
        vm.Rules.Clear();
        var rule = vm.AddRule("user=(\\w+", isRegex: true);

        Assert.NotNull(rule.Error);
        Assert.Null(vm.Highlighter.Resolve("user=bob"));

        rule.Pattern = "user=(\\w+)";
        Assert.Null(rule.Error);
        Assert.NotNull(vm.Highlighter.Resolve("user=bob"));
    }

    [Fact]
    public void A_regex_that_times_out_counts_as_no_match_and_later_rules_still_apply()
    {
        var vm = Create();
        vm.Rules.Clear();
        var fallback = vm.AddRule("x");
        vm.AddRule("(a+)+$", isRegex: true); // catastrophic backtracking on "aaaa…x"

        var style = vm.Highlighter.Resolve(new string('a', 40) + "x");

        Assert.Equal(Background(fallback), style!.Background!.Color);
    }

    [Fact]
    public void An_added_rule_has_colors_for_both_themes()
    {
        var vm = Create();

        var rule = vm.AddRule("needle");

        Assert.NotNull(rule.LightBackground);
        Assert.NotNull(rule.DarkBackground);
        Assert.NotEqual(rule.LightBackground, rule.DarkBackground);
        Assert.NotNull(vm.Highlighter.Resolve("a needle in an ERROR line")!.Background);
    }

    [Fact]
    public void Rules_and_their_order_are_saved_and_restored()
    {
        var vm = Create();
        var rule = vm.AddRule("needle");
        rule.LightForeground = "#112233";
        vm.MoveRuleDownCommand.Execute(rule);

        var restarted = Create();

        Assert.Equal(4, restarted.Rules.Count);
        Assert.Equal("needle", restarted.Rules[1].Pattern);
        Assert.Equal("#112233", restarted.Rules[1].LightForeground);
    }

    [Fact]
    public void Presets_can_be_created_renamed_switched_and_deleted_and_are_saved()
    {
        var vm = Create();

        var mine = vm.CreatePreset("Payments")!;
        Assert.Same(mine, vm.ActivePreset);
        Assert.Equal(3, vm.Rules.Count); // a copy of the rules that were active
        vm.Rules.Clear();
        vm.AddRule("card declined");
        Assert.Null(vm.Highlighter.Resolve("ERROR boom"));

        Assert.True(vm.RenamePreset("Payment service"));
        Assert.False(vm.RenamePreset(HighlightingViewModel.DefaultPresetName)); // names are unique
        Assert.Null(vm.CreatePreset("  "));

        var restarted = Create();
        Assert.Equal(["Default", "Payment service"], restarted.Presets.Select(p => p.Name));
        Assert.Equal("Payment service", restarted.ActivePreset!.Name);
        Assert.NotNull(restarted.Highlighter.Resolve("card declined"));

        restarted.ActivePreset = restarted.Presets[0];
        Assert.NotNull(restarted.Highlighter.Resolve("ERROR boom"));
        Assert.Null(restarted.Highlighter.Resolve("card declined"));
        restarted.ActivePreset = restarted.Presets[1];

        restarted.DeletePresetCommand.Execute(null);
        Assert.Equal("Default", restarted.ActivePreset!.Name);
        Assert.False(restarted.DeletePresetCommand.CanExecute(null)); // the last preset stays

        var again = Create();
        Assert.Equal(["Default"], again.Presets.Select(p => p.Name));
        Assert.Equal("Default", again.ActivePreset!.Name);
    }

    [Fact]
    public void A_corrupt_presets_file_falls_back_to_the_default_preset()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, HighlightPresetsFile.FileName), "{ not json");

        var vm = Create();

        Assert.Equal(HighlightingViewModel.DefaultPresetName, vm.ActivePreset!.Name);
        Assert.NotNull(vm.Highlighter.Resolve("ERROR boom"));
    }

    private static Color Background(HighlightRuleViewModel rule) =>
        (Color)ColorConverter.ConvertFromString(rule.LightBackground!);
}
