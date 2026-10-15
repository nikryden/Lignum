# 07: Long lines and the detail pane

**What to build:** A single huge line (for example a minified JSON dump) cannot freeze the viewer: the display is truncated after about 10,000 characters, with a "show full line" action. A collapsible detail pane, docked right by default, shows the selected line in full, word-wrapped and read-only, and supports copy. Search and filters (later tickets) must still see the full line, so truncation is display-only.

**Blocked by:** 04

**Status:** ready-for-agent

- [ ] Lines over the threshold are truncated on screen with a visible marker
- [ ] "Show full line" opens the line in the detail pane
- [ ] The detail pane shows the selected line wrapped, can be collapsed, can be docked right or bottom, and supports copy
- [ ] A test file with a multi-MB single line renders and scrolls without freezing
- [ ] `LogDocument` returns the full text of a long line regardless of the display limit (tested)
