# 23: Bookmarks pane: description editing, links and layout

**What to build:** Edit and copy a bookmark's description from the pane, open web links written in descriptions, and make the pane compact and resizable.

**Blocked by:** 22

**Status:** done

- [x] Right-click a bookmark in the pane: "Edit description…" opens the description dialog (selectable, copy and paste), "Copy description", "Remove bookmark"
- [x] Links in a description open with Ctrl+left click: `http(s)://` in the default browser, a drive or UNC path (`C:\…`, `C:/…`, `\server\…`; quoted when it has spaces) in a tab, a folder in Explorer; a missing path is reported when clicked
- [x] The pane's right-click menu adds "Open …" per link and "Show … in folder" per path
- [x] The pane is resizable by dragging its left edge
- [x] Smaller text and compact filter controls; the color filter is a row of color dots (faded = hidden)
- [x] Tests for link detection (URLs, both slash styles, UNC, quoted, trailing punctuation) and opening each kind
- [x] The pane width is saved (app-wide `bookmarksPaneWidth`) and "Edit bookmark note…" is now "Edit description…" everywhere
