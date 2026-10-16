# 17: Bookmarks

**What to build:** The user bookmarks lines with Ctrl+F2 or "Toggle bookmark" in the right-click menu, sees markers in the margin, and jumps between bookmarks with F2 / Shift+F2. Bookmarks are saved per file (keyed by a hash of the file path) and restored when the file is reopened. The storage schema leaves room for a note on each bookmark, which is a wanted future feature.

**Blocked by:** 13, 15

**Status:** resolved

- [x] Toggle, next and previous work, including wrap-around (view-model tests)
- [x] Margin markers are shown in the main view
- [x] Bookmarks persist per file and are restored on reopen (tested with a temp settings folder)
- [x] The bookmark JSON schema has room for an optional note field without needing migration

## Comments

Bookmarks live per tab (`LogTabViewModel.Bookmarks`, `BookmarksViewModel`) and save on every toggle to `bookmarks/<sha256 of upper-cased full path>.json` as `{ path, bookmarks: [ { line } ] }`; unknown fields (a future `note`) are preserved on load/save, so no migration is needed. Ctrl+F2 / F2 / Shift+F2 are window KeyBindings (plus Ctrl+F2 on the results view), the right-click "Toggle bookmark" items and a Go menu section are bound.
Margin dots: checked in an off-screen STA render of MainWindow (marker drawn on the bookmarked line); not checked visually in the themed running app.
