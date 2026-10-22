# 14: Highlight a row with custom foreground and background colors

**What to build:** Highlight individual lines by hand, with a chosen text color and background color. Rule-based highlights already support both colors (`LineStyle` in `LineHighlighter.cs`); this adds manual per-line highlights.

**Blocked by:** None

**Status:** done

**Storage:** The per-file bookmarks file gets a separate optional `highlights` array, `{ "line": 12, "foreground": "#..", "background": "#.." }`. A highlighted line is not necessarily bookmarked. Unknown fields are already kept on save, so there is no migration.

- [x] Line context menu: "Highlight line…" (pick foreground and background, both optional) and "Remove highlight"
- [x] Works on a multi-line selection in the main view (at most 10,000 lines; more points to highlight rules)
- [x] Manual highlights draw over rule highlights; the selection still draws over both
- [x] Saved per file and restored on reopen (test); old files load unchanged (test)
- [x] Included in `.lm` export
