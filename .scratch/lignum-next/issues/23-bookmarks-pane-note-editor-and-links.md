# 23: Bookmarks pane: description editing, links and layout

**What to build:** Edit and copy a bookmark's description from the pane, open web links written in descriptions, and make the pane compact and resizable.

**Blocked by:** 22

**Status:** done

- [x] Right-click a bookmark in the pane: "Edit description…" opens the description dialog (selectable, copy and paste), "Copy description", "Remove bookmark"
- [x] `http(s)://` links in a description are shown as links and open in the default browser with Ctrl+left click; file paths are plain text (owner's decision after the first version)
- [x] The pane is resizable by dragging its left edge
- [x] Smaller text and compact filter controls; the color filter is a row of color dots (faded = hidden)
- [x] Tests for link detection (URLs, trailing punctuation, paths are not links) and opening in the browser
