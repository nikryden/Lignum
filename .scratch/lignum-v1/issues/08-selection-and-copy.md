# 08: Selection and copy

**What to build:** The user selects text character by character by dragging, extends the selection with Shift+Click (even across millions of lines), selects everything with Ctrl+A, and copies with Ctrl+C. Selection is stored as file positions (line, column), independent of what is on screen. Copying streams the text through a new `LogDocument` text-range reader and never freezes the UI. Above about 50 MB the app asks for confirmation or offers to save the selection to a file instead.

**Blocked by:** 04

**Status:** resolved

- [x] Drag, Shift+Click, keyboard Shift+arrows and Ctrl+A select text; the selection survives scrolling
- [x] Ctrl+C copies exactly the selected text; multi-line selections keep the original line endings
- [x] `LogDocument` streams the text between two (line, column) positions (tested, including across block boundaries)
- [x] Above the threshold the user is asked to confirm or save to file; copying runs without blocking the UI
- [x] View-model tests cover the selection model and copying

## Comments

Core: `FilePosition(Line, Column)` and `LogDocument.ReadTextRange(start, end, TextWriter, ct)` (first/last line decoded, the lines between streamed raw in 1 MB chunks through a stateful decoder, so `
`/`
` are kept as stored), plus `MeasureTextRange` (index-only byte estimate for the 50 MB prompt). App: `TextSelection` (anchor + caret as file positions), `SelectionCopier` (background read, clipboard/prompt/save-to-file behind `IClipboardService`/`ICopyPrompt`), `LogTabViewModel.Selection.cs` (Copy/SelectAll/CancelCopy, `CopyMessage`), `LogView.Selection.cs` (drawing, drag with auto-scroll, Shift+Click, Shift+arrows/Home/End/PageUp/PageDown), tab-aware column mapping in `TabExpansion`.
Manual checks: drag selection and Shift+arrow extension were seen drawn correctly in a smoke run before ticket 16 merged. Ctrl+C/Ctrl+A now live as KeyBindings on `LogTabView` (Edit menu too) and are covered by view-model tests, but the real keypress path, the large-copy dialog and SaveFileDialog were not exercised in the running app (synthetic input was stopped): please check by hand.
