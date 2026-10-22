# 11: Remove all bookmarks from a file

**What to build:** One action that removes every bookmark (and its note and color) from the current file.

**Blocked by:** None

**Status:** done

- [x] "Clear all bookmarks" in the Bookmarks menu and the margin/line context menu
- [x] Asks for confirmation when any bookmark has a note
- [x] The per-file bookmarks file is saved empty (view-model test)
- [x] MCP: already covered by `remove_bookmark` with `all=true`, so no new tool
- [x] Disabled when the tab has no bookmarks
