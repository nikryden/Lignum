using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Viewer;
using Lignum.Core;

namespace Lignum.App.ViewModels;

// Filter rules (ticket 12): include / exclude rules whose combined result fills the results pane. Any
// change to the list or to a rule re-runs the filter, cancelling the previous run.
public sealed partial class SearchViewModel
{
    private ObservableCollection<FilterRuleViewModel>? _filterRules;
    private TextSelection? _resultsSelection;
    private bool _suspendFilter;

    /// <summary>The add box: text of the next rule.</summary>
    [ObservableProperty]
    private string _newFilterText = "";

    [ObservableProperty]
    private bool _newFilterIsRegex;

    [ObservableProperty]
    private bool _newFilterMatchCase;

    /// <summary>Why the filter cannot run (an invalid regex in a rule); null when it is fine.</summary>
    [ObservableProperty]
    private string? _filterError;

    [ObservableProperty]
    private bool _isFilterBarOpen;

    /// <summary>The tab's filter rules, in the order the user added them.</summary>
    public ObservableCollection<FilterRuleViewModel> FilterRules => _filterRules ??= CreateFilterRules();

    /// <summary>The selection in the results pane, as file positions; copied with <see cref="LogTabViewModel.CopyResultsCommand"/>.</summary>
    public TextSelection ResultsSelection => _resultsSelection ??= new TextSelection();

    /// <summary>The query the results pane shows (tests).</summary>
    internal QueryResult? ActiveQuery => _result;

    /// <summary>
    /// Adds a filter rule and re-runs the filter. The entry point for "add as include / exclude filter"
    /// from a selection (ticket 15) as well as the add box.
    /// </summary>
    public FilterRuleViewModel AddFilterRule(QueryRule rule)
    {
        var item = new FilterRuleViewModel(rule);
        FilterRules.Add(item);
        return item;
    }

    /// <summary>The rules as plain records, for saving (session, history).</summary>
    public IReadOnlyList<QueryRule> GetFilterRules() => [.. FilterRules.Select(r => r.ToRule())];

    /// <summary>Replaces every rule (session restore) and runs the filter once.</summary>
    public void SetFilterRules(IEnumerable<QueryRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _suspendFilter = true;
        try
        {
            foreach (var old in FilterRules) old.PropertyChanged -= OnFilterRuleChanged; // Clear() reports no old items
            FilterRules.Clear();
            foreach (var rule in rules) FilterRules.Add(new FilterRuleViewModel(rule));
        }
        finally
        {
            _suspendFilter = false;
        }

        ApplyFilter();
    }

    [RelayCommand]
    private void RemoveFilterRule(FilterRuleViewModel? rule)
    {
        if (rule is not null) FilterRules.Remove(rule);
    }

    [RelayCommand]
    private void AddIncludeFilter() => AddFromBox(RuleKind.Include);

    [RelayCommand]
    private void AddExcludeFilter() => AddFromBox(RuleKind.Exclude);

    [RelayCommand]
    private void ToggleFilterBar() => IsFilterBarOpen = !IsFilterBarOpen;

    /// <summary>Selects every row of the results pane.</summary>
    [RelayCommand]
    private void SelectAllResults()
    {
        if (Results is not { RowCount: > 0 } source) return;
        var first = source.Lines[0];
        var last = source.Lines[source.RowCount - 1];
        ResultsSelection.MoveCaret(new FilePosition(last, int.MaxValue), extend: false);
        ResultsSelection.MoveCaret(new FilePosition(first, 0), extend: true);
    }

    private void AddFromBox(RuleKind kind)
    {
        if (string.IsNullOrEmpty(NewFilterText)) return;
        AddFilterRule(new QueryRule(NewFilterText, kind, NewFilterIsRegex, NewFilterMatchCase));
        NewFilterText = "";
    }

    private ObservableCollection<FilterRuleViewModel> CreateFilterRules()
    {
        var rules = new ObservableCollection<FilterRuleViewModel>();
        rules.CollectionChanged += OnFilterRulesChanged;
        return rules;
    }

    private void OnFilterRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is { } removed)
        {
            foreach (FilterRuleViewModel rule in removed) rule.PropertyChanged -= OnFilterRuleChanged;
        }

        if (e.NewItems is { } added)
        {
            foreach (FilterRuleViewModel rule in added) rule.PropertyChanged += OnFilterRuleChanged;
        }

        if (!_suspendFilter) ApplyFilter();
    }

    private void OnFilterRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FilterRuleViewModel.IsExclude)) ApplyFilter();
    }

    /// <summary>
    /// Runs the current rules. With no active rule the filter's results are cleared (find all results stay);
    /// an invalid regex sets <see cref="FilterError"/> and keeps whatever is shown.
    /// </summary>
    private void ApplyFilter()
    {
        if (_filterRules is null) return;
        var rules = GetFilterRules();
        if (!rules.Any(r => r.IsEnabled && !string.IsNullOrEmpty(r.Text)))
        {
            FilterError = null;
            if (_showingFilter) ClearResults();
            return;
        }

        if (_document is not { } document) return;

        Query query;
        try
        {
            query = new Query(rules, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            FilterError = ex.Message;
            return;
        }

        FilterError = null;
        _ = RunQuery(document, query, isFilter: true); // never faults: Completion does not
    }
}
