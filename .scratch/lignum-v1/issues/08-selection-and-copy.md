# 08: Selection and copy

**What to build:** The user selects text character by character by dragging, extends the selection with Shift+Click (even across millions of lines), selects everything with Ctrl+A, and copies with Ctrl+C. Selection is stored as file positions (line, column), independent of what is on screen. Copying streams the text through a new `LogDocument` text-range reader and never freezes the UI. Above about 50 MB the app asks for confirmation or offers to save the selection to a file instead.

**Blocked by:** 04

**Status:** ready-for-agent

- [ ] Drag, Shift+Click, keyboard Shift+arrows and Ctrl+A select text; the selection survives scrolling
- [ ] Ctrl+C copies exactly the selected text; multi-line selections keep the original line endings
- [ ] `LogDocument` streams the text between two (line, column) positions (tested, including across block boundaries)
- [ ] Above the threshold the user is asked to confirm or save to file; copying runs without blocking the UI
- [ ] View-model tests cover the selection model and copying
