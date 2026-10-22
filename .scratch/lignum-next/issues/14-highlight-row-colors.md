# 14: Highlight a row with custom foreground and background colors

**What to build:** Highlight individual lines by hand, with a chosen text color and background color. Rule-based highlights already support both colors (`LineStyle` in `LineHighlighter.cs`); this adds manual per-line highlights.

**Blocked by:** None

**Status:** ready-for-agent

**Storage:** The per-file bookmarks file gets a separate optional `highlights` array, `{ "line": 12, "foreground": "#..", "background": "#.." }`. A highlighted line is not necessarily bookmarked. Unknown fields are already kept on save, so there is no migration.

- [ ] Line context menu: "Highlight line…" (pick foreground and background, both optional) and "Remove highlight"
- [ ] Works on a multi-line selection
- [ ] Manual highlights draw over rule highlights; the selection still draws over both
- [ ] Saved per file and restored on reopen (test); old files load unchanged (test)
- [ ] Included in `.lm` export
