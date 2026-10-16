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

Core: `FilePosition(Line, Column)` (now the one shared type; ticket 05's App copy was removed) and `LogDocument.ReadTextRange(start, end, TextWriter, ct)`: the first and last lines are decoded in full, the lines between are streamed raw in 1 MB chunks through a stateful decoder, so CRLF/LF breaks are kept as stored and lines over the 10,000-char display limit are copied whole. `MeasureTextRange` gives an index-only size estimate for the 50 MB prompt. App: `TextSelection` (anchor + caret), `SelectionCopier` (background read, then clipboard; prompt/save-to-file behind `IClipboardService`/`ICopyPrompt`), `LogTabViewModel.Selection.cs` (Copy/SelectAll/CancelCopy, `CopyMessage`; the selection caret is kept equal to `Navigation.Caret`, and a plain caret move clears the selection), `LogView.Selection.cs` (drawing, drag with auto-scroll, Shift+Click, Shift+arrows/Home/End/PageUp/PageDown).
Manual checks still needed: drag and Shift+arrow drawing were seen working in one smoke run before tickets 16/05/07 were merged. After those merges the app was not run again, because synthetic input and real %APPDATA% are now off limits. Please check by hand: drag, Shift+Click, Shift+arrows, Ctrl+A/Ctrl+C keypresses (KeyBindings on `LogTabView`, also in the Edit menu), the large-copy dialog and SaveFileDialog. Shift+End on a line cut at 10,000 chars stops at the displayed end (Ctrl+A copies the full line).
