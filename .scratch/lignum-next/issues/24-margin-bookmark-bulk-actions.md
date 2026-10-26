# 24: Margin bookmark column: bulk actions by color and note

**What to build:** Bulk actions on the main view's bookmark margin.

**Blocked by:** 22 (shares the per-tab color filter)

**Status:** done

- [x] "Remove bookmarks without notes"
- [x] "Remove bookmarks of color ▸ [colors]"
- [x] "Show only colors ▸ [checkboxes]": hides the other colors' margin markers only (no line filtering); F2 / Shift+F2 skip hidden bookmarks. Same per-tab setting as the pane's color filter (ticket 22)
- [x] In the margin/line context menu and the Bookmarks menu
- [x] Removing asks for confirmation when any removed bookmark has a note (as ticket 11)
- [x] View-model tests for both removals and for navigation with hidden colors
