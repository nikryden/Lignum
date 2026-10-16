# 12: Filter rules

**What to build:** The user builds a list of include and exclude filter rules. Each rule has text or regex, a case setting and an on/off toggle. A line is shown when (there are no enabled include rules, or it matches at least one enabled include rule) and it matches no enabled exclude rule. The filtered lines appear in the results pane (the same pane and engine as find all), and clicking one jumps the main view to it. Changing the rules re-runs the query.

**Blocked by:** 11

**Status:** resolved

- [x] Rule combination is tested: include only, exclude only, both, disabled rules, no rules
- [x] Rules support text or regex and case sensitivity
- [x] Toggling, adding or removing a rule updates the results (cancelling the previous query)
- [x] Filtered lines can be selected and copied from the results pane
- [x] The filter UI makes include and exclude visually distinct

## Comments

Core combination matrix in `FilterRuleTests` (engine already existed from 10); per-tab rule list in `SearchViewModel.Filter.cs` (`FilterRules` of `FilterRuleViewModel`, `AddFilterRule(QueryRule)`, `Get/SetFilterRules` as plain `QueryRule` records), sharing the results pane with find all (last run wins). Any rule change re-runs the filter and cancels the previous query; an invalid regex sets `FilterError` and keeps the shown results.
Results pane now has its own selection; Ctrl+C there (`LogTabViewModel.CopyResultsCommand`) copies only the selected result lines, Ctrl+A selects all results. Filter bar (Search > Filter rules) shows green include / red exclude chips.
Manual-only, not verified (no settings-folder override for manual runs): the filter bar's look in both themes, focus behaviour, and click/drag/Ctrl+C in the results pane.
