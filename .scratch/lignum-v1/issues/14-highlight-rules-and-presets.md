# 14: Highlight rules and presets

**What to build:** User-defined coloring rules color whole lines (foreground and/or background) from text or regex matches. When rules overlap, the first in the list wins, and the user can reorder them. A default preset colors ERROR, WARN and INFO, with colors defined for both themes. Rule sets can be saved as named presets. Search matches are drawn on top of the line coloring.

**Blocked by:** 10, 13

**Status:** ready-for-agent

- [ ] Rule resolution (first match wins, disabled rules skipped) is tested at the view-model level
- [ ] The default ERROR/WARN/INFO preset is active on first run and readable in both themes
- [ ] Presets can be created, renamed, deleted and switched, and are saved to JSON
- [ ] Search match highlighting stays visible on colored lines
- [ ] Scrolling performance is unaffected with several rules active
