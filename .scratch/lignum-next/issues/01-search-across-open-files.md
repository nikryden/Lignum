# 01: Search across open files

**What to build:** Run find all, or a filter, across every open tab at once, not just the current one. The results list shows which file each match comes from. Clicking a match switches to that tab and jumps to the line.

**Blocked by:** None (can start immediately)

**Status:** done

- [x] One search runs over all open tabs in the background, can be cancelled, and reports progress
- [x] Each result shows the file name and the original line number
- [x] Clicking a result opens that tab at the matching line
- [x] Text, regex and case options work the same as single-file search

## Open questions
- Does it reuse each tab's results pane, or need a new shared pane?
- Should results from different files be merged by timestamp? Timestamp parsing is out of scope for v1.

## Decisions (2026-10-17)
- Results go in a new shared pane by default. A toggle switches to "per tab": the search then runs in each tab's own results pane instead.
- Results are grouped by file. Merging by timestamp waits until timestamp parsing exists.
