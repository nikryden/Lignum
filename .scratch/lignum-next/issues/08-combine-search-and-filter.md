# 08: Combine search and filter

**What to build:** Make search and filter rules work together instead of replacing each other. Today (v1) find all and filter share one results pane, and whichever ran last owns it (v1 ticket 12). Instead, the filter rules narrow the log first, and the search then runs only inside the filtered lines.

Example: exclude rule `HealthCheck`, include rule `OrderService`, then search `timeout`. The result is the OrderService lines that contain "timeout", with health checks hidden.

**Blocked by:** None (can start immediately)

**Status:** done

- [x] The results pane shows lines that pass the filter rules (v1 combination rule) AND match the search, when both are active
- [x] With only filter rules, or only a search, it behaves as it does in v1
- [x] F3 / Shift+F3 and the match count only step through and count matches inside the filtered lines
- [x] Changing a filter rule or the search re-runs the combined query and cancels the previous run
- [x] The results pane header shows what is active (for example "Filter: 2 rules · Search: timeout · 1,234 lines")
- [x] Core: one query that combines filter rules and the find pattern, run in a single pass over the file (tested through `LogDocument`, including during indexing)
- [x] Session restore (v1 ticket 18) saves and restores both together

## Open questions
- Should the main (full log) view also be able to hide non-matching lines, or does the combined result stay in the results pane only?
- Should a search highlight matches in the full log even when they are filtered out?
- Is a quick toggle wanted to switch the search between "inside filter" and "whole file"?

## Decisions (2026-10-17)
- Main view: the combined result shows in the results pane, and the main (full log) view can also hide the non-matching lines. This is a toggle, off by default.
- Search matches in lines the filter hides are highlighted in the full log. This is a setting.
- A quick toggle switches the search between "inside the filter" and "the whole file".
