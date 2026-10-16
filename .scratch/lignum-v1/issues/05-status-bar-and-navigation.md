# 05: Status bar and navigation

**What to build:** A status bar shows the current line and column, the total line count, the file size, and indexing progress with a cancel button. Ctrl+G opens a "Go to line" prompt, and Ctrl+Home / Ctrl+End jump to the start or end of the file. A caret position is tracked so line and column are meaningful.

**Blocked by:** 04

**Status:** resolved

- [x] The status bar shows line/column, total line count, file size and indexing progress
- [x] The cancel button stops indexing
- [x] Ctrl+G jumps to the given line; out-of-range or invalid input is handled cleanly
- [x] Ctrl+Home / Ctrl+End jump to the first or last line
- [x] View-model tests cover go-to-line and caret position updates

## Comments

Caret is `Viewer/FilePosition(Line, Column)` (0-based file line, character index), owned by `NavigationViewModel` (go to line with validation, Ctrl+Home/End, `CaretText`, `RevealRequested`) and bound two-way to `LogView.Caret`; click, arrows, Home/End, PageUp/PageDown move it. Status bar shows Ln/Col, line count, file size (`ByteSize`) and the existing indexing progress + Cancel. Ctrl+G opens `Views/GoToLineDialog`, which stays open with a message on bad input.
Covered by `NavigationViewModelTests`, `StatusBarTests`, `CaretMappingTests`. The drawing (caret bar, dialog, key routing past the ScrollViewer) was not visually verified: the shared desktop had other agents' Lignum windows on top, so screenshots were unusable. Needs a manual check.
