# 10: Find all and the results pane

**What to build:** Ctrl+F opens a find box. Plain-text "find all" lists every matching line in a results pane below the main view, split off by a draggable divider that can be collapsed. Results show their original line numbers. Clicking a result scrolls the main view to that line. F3 / Shift+F3 step through matches. Matched text is highlighted in both views, and the match count, progress and a cancel button appear in the status bar. A search started while indexing is still running keeps adding results as indexing continues. The results pane reuses the viewer control with a line source made from the result set. `LogDocument` gains a query API that returns a growing, ordered result set; plain text uses vectorized span `IndexOf`, running in parallel chunks.

**Blocked by:** 04

**Status:** ready-for-agent

- [ ] The `LogDocument` query returns ordered matching line numbers, with progress and cancellation (tested)
- [ ] A query started during indexing returns the same final results as one started afterwards (tested)
- [ ] The results pane uses the same viewer control and shows the original line numbers
- [ ] Clicking a result scrolls the main view to that line and selects it
- [ ] F3 / Shift+F3 navigate matches; matched text is highlighted; the match count is shown
- [ ] The split divider can be dragged and the pane collapsed
- [ ] Matches inside display-truncated long lines are still found
