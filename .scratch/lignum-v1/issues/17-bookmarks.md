# 17: Bookmarks

**What to build:** The user bookmarks lines with Ctrl+F2 or "Toggle bookmark" in the right-click menu, sees markers in the margin, and jumps between bookmarks with F2 / Shift+F2. Bookmarks are saved per file (keyed by a hash of the file path) and restored when the file is reopened. The storage schema leaves room for a note on each bookmark, which is a wanted future feature.

**Blocked by:** 13, 15

**Status:** ready-for-agent

- [ ] Toggle, next and previous work, including wrap-around (view-model tests)
- [ ] Margin markers are shown in the main view
- [ ] Bookmarks persist per file and are restored on reopen (tested with a temp settings folder)
- [ ] The bookmark JSON schema has room for an optional note field without needing migration
