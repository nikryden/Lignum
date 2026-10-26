# 22: Bookmarks pane: count, sort and filter

**What to build:** Make the per-file bookmarks pane (ticket 06) easier to use with many bookmarks: a count, sorting, and filters by color, by "has a note" and by note text.

**Blocked by:** None

**Status:** done

- [x] The pane header shows the count: "Bookmarks (12)", or "Bookmarks (4 of 12)" while a filter hides some
- [x] Sort by line (default) or by color (then line)
- [x] Filter by one or more colors (a toggle per color; all on by default). This color filter is per tab and shared with the margin's "Show only colors" (ticket 24)
- [x] "Notes only" toggle: only bookmarks with a note
- [x] Search box over the notes, case-insensitive, matching anywhere; `*` matches any run of characters (`disk*full`). Plain substring search with wildcards, no regex
- [x] Filters are view state only: not saved, and they never change the bookmarks file
- [x] View-model tests for the count, sort, color filter, notes-only and wildcard search
