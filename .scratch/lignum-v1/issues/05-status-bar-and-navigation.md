# 05: Status bar and navigation

**What to build:** A status bar shows the current line and column, the total line count, the file size, and indexing progress with a cancel button. Ctrl+G opens a "Go to line" prompt, and Ctrl+Home / Ctrl+End jump to the start or end of the file. A caret position is tracked so line and column are meaningful.

**Blocked by:** 04

**Status:** ready-for-agent

- [ ] The status bar shows line/column, total line count, file size and indexing progress
- [ ] The cancel button stops indexing
- [ ] Ctrl+G jumps to the given line; out-of-range or invalid input is handled cleanly
- [ ] Ctrl+Home / Ctrl+End jump to the first or last line
- [ ] View-model tests cover go-to-line and caret position updates
