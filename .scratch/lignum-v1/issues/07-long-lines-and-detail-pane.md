# 07: Long lines and the detail pane

**What to build:** A single huge line (for example a minified JSON dump) cannot freeze the viewer: the display is truncated after about 10,000 characters, with a "show full line" action. A collapsible detail pane, docked right by default, shows the selected line in full, word-wrapped and read-only, and supports copy. Search and filters (later tickets) must still see the full line, so truncation is display-only.

**Blocked by:** 04

**Status:** resolved

- [x] Lines over the threshold are truncated on screen with a visible marker
- [x] "Show full line" opens the line in the detail pane
- [x] The detail pane shows the selected line wrapped, can be collapsed, can be docked right or bottom, and supports copy
- [x] A test file with a multi-MB single line renders and scrolls without freezing
- [x] `LogDocument` returns the full text of a long line regardless of the display limit (tested)

## Comments
- Core: `LogDocument.ReadLinePrefix(es)(…, maxChars)` read only the bytes needed and return `LinePrefix(Text, IsTruncated)`. `ReadLine`/`ReadLines` still return the full line (tested with a 5 MB line). `LineSource.DisplayLimit` = 10,000. Truncated rows end with a clickable " … [truncated: show full line]" marker. View > "Show full line" also opens the pane.
- Detail pane: the line shown is per tab (`LogTabViewModel.Detail`, follows the ticket 05 caret). The layout is app-wide (`MainViewModel.DetailPane`: open/collapsed, dock right/bottom, size, all saved in settings.json `detailPane`). Copy works on a text selection (Ctrl+C) and with a "Copy line" button that copies the whole line. The text box shows at most 250,000 characters and a notice when there are more. In a smoke test a 1M-char TextBox took about 1.2 s of layout.
- Manual checks: before parallel work was restricted, I smoke-tested a 5 MB single line in the real app. Rendering, horizontal scrolling, the marker click, the detail pane, and docking bottom and back right all worked without freezing. I did not visually re-check after merging tickets 16 and 05 (synthetic input is no longer allowed). Re-check the marker click, caret-follow and dock/collapse by hand.
