# 12: Filter rules

**What to build:** The user builds a list of include and exclude filter rules. Each rule has text or regex, a case setting and an on/off toggle. A line is shown when (there are no enabled include rules, or it matches at least one enabled include rule) and it matches no enabled exclude rule. The filtered lines appear in the results pane (the same pane and engine as find all), and clicking one jumps the main view to it. Changing the rules re-runs the query.

**Blocked by:** 11

**Status:** ready-for-agent

- [ ] Rule combination is tested: include only, exclude only, both, disabled rules, no rules
- [ ] Rules support text or regex and case sensitivity
- [ ] Toggling, adding or removing a rule updates the results (cancelling the previous query)
- [ ] Filtered lines can be selected and copied from the results pane
- [ ] The filter UI makes include and exclude visually distinct
