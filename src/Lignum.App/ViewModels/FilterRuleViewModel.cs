using CommunityToolkit.Mvvm.ComponentModel;
using Lignum.Core;

namespace Lignum.App.ViewModels;

/// <summary>
/// One editable filter rule in a tab's filter list. The persisted form is the plain <see cref="QueryRule"/>
/// record (<see cref="ToRule"/>); this wrapper only exists so the view can bind and edit it. Any change is
/// reported through <see cref="ObservableObject.PropertyChanged"/>, which re-runs the filter.
/// </summary>
/// <remarks>Not to be confused with highlight rules (ticket 14), which color lines and have their own list.</remarks>
public sealed partial class FilterRuleViewModel : ObservableObject
{
    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExclude))]
    private RuleKind _kind;

    [ObservableProperty]
    private bool _isRegex;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _nonBacktracking;

    public FilterRuleViewModel(QueryRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _text = rule.Text;
        _kind = rule.Kind;
        _isRegex = rule.IsRegex;
        _matchCase = rule.MatchCase;
        _isEnabled = rule.IsEnabled;
        _nonBacktracking = rule.NonBacktracking;
    }

    /// <summary>True for an exclude rule (the view styles include and exclude rules differently).</summary>
    public bool IsExclude => Kind == RuleKind.Exclude;

    public QueryRule ToRule() => new(Text, Kind, IsRegex, MatchCase, IsEnabled, NonBacktracking);
}
