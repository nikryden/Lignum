using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Lignum.Core;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Search and filter history (ticket 18): app-wide, last 20, most recent first, kept in settings.json.</summary>
public sealed class HistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public HistoryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>A fresh app start: settings are read from disk again.</summary>
    private MainViewModel Start()
    {
        var vm = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(vm);
        return vm;
    }

    private async Task<LogTabViewModel> Open(MainViewModel vm, string name = "app.log")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "alpha\nbeta\ngamma\n");
        await vm.OpenFileAsync(path);
        return vm.SelectedTab!;
    }

    private static async Task Find(LogTabViewModel tab, string text)
    {
        tab.Search.FindText = text;
        await tab.Search.FindAllCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Searched_terms_are_listed_most_recent_first_without_duplicates()
    {
        var vm = Start();
        var tab = await Open(vm);

        await Find(tab, "alpha");
        await Find(tab, "beta");
        await Find(tab, "alpha");

        Assert.Equal(["alpha", "beta"], vm.History.SearchTerms);
    }

    [Fact]
    public async Task Search_history_is_capped_at_20()
    {
        var vm = Start();
        var tab = await Open(vm);

        for (var i = 0; i < 25; i++) await Find(tab, $"term{i}");

        Assert.Equal(SearchHistory.MaxEntries, vm.History.SearchTerms.Count);
        Assert.Equal(20, SearchHistory.MaxEntries);
        Assert.Equal("term24", vm.History.SearchTerms[0]);
        Assert.Equal("term5", vm.History.SearchTerms[^1]);
    }

    [Fact]
    public async Task Filter_rules_are_remembered_most_recent_first_without_duplicates_and_capped()
    {
        var vm = Start();
        var tab = await Open(vm);

        for (var i = 0; i < 22; i++) tab.Search.AddFilterRule(new QueryRule($"rule{i}"));
        tab.Search.NewFilterText = "rule3";
        tab.Search.AddExcludeFilterCommand.Execute(null);

        Assert.Equal(20, vm.History.FilterTerms.Count);
        Assert.Equal(["rule3", "rule21", "rule20"], vm.History.FilterTerms.Take(3));
        Assert.Single(vm.History.FilterTerms, t => t == "rule3");
    }

    [Fact]
    public async Task History_is_shared_by_tabs_and_survives_a_restart()
    {
        var vm = Start();
        var first = await Open(vm, "a.log");
        var second = await Open(vm, "b.log");
        await Find(first, "alpha");
        await Find(second, "beta");
        second.Search.AddFilterRule(new QueryRule("gamma", RuleKind.Exclude));

        var restarted = Start();

        Assert.Equal(["beta", "alpha"], restarted.History.SearchTerms);
        Assert.Equal(["gamma"], restarted.History.FilterTerms);
    }

    [Fact]
    public async Task Empty_and_invalid_searches_are_not_remembered()
    {
        var vm = Start();
        var tab = await Open(vm);

        await Find(tab, "");
        tab.Search.IsRegex = true;
        await Find(tab, "(unclosed");

        Assert.Empty(vm.History.SearchTerms);
    }
}
