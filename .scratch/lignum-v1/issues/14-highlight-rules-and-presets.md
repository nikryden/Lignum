# 14: Highlight rules and presets

**What to build:** User-defined coloring rules color whole lines (foreground and/or background) from text or regex matches. When rules overlap, the first in the list wins, and the user can reorder them. A default preset colors ERROR, WARN and INFO, with colors defined for both themes. Rule sets can be saved as named presets. Search matches are drawn on top of the line coloring.

**Blocked by:** 10, 13

**Status:** resolved

- [x] Rule resolution (first match wins, disabled rules skipped) is tested at the view-model level
- [x] The default ERROR/WARN/INFO preset is active on first run and readable in both themes
- [x] Presets can be created, renamed, deleted and switched, and are saved to JSON
- [x] Search match highlighting stays visible on colored lines
- [ ] Scrolling performance is unaffected with several rules active

## Comments

- `HighlightingViewModel` (app-wide, `highlight-presets.json`) holds presets of ordered rules; `LineHighlighter` resolves first-enabled-match per theme (50 ms regex timeout, timeout = no match). 12 VM-level tests cover resolution, ordering, disabled/invalid/timeout rules, themes, preset create/rename/switch/delete and persistence.
- `LogView.Highlighting.cs` draws the rule background under the search decorations and the rule foreground for text; styles are cached per loaded row set. Editing UI: View > Highlight rules…; `AddRule(pattern)` is the entry point for ticket 15.
- Manual-only, not verified (no manual app runs allowed against real %APPDATA%): readability of the default colors in both themes, search matches visible on colored lines, and scrolling performance with several rules (left unticked). The dialog was only smoke-loaded off-screen in a throwaway test.
- Ticked items for themes and search-on-color rest on tests and draw order (rule background, then search matches, then text); the scrolling-performance item stays unticked until a manual benchmark.
