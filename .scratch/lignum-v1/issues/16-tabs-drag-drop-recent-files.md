# 16: Tabs, drag-and-drop and recent files

**What to build:** Several logs can be open at once in tabs, each with its own view, results pane and filters. Opening a file that is already open switches to its tab. Ctrl+W closes the current tab and releases its file handle. Files can be dragged onto the window to open them. A recent-files list (saved in settings) reopens logs quickly.

**Blocked by:** 13

**Status:** ready-for-agent

- [ ] Multiple tabs work independently (search/filter state per tab)
- [ ] Opening an already-open path (compared case-insensitively, after resolving the full path) switches to its tab (view-model test)
- [ ] Ctrl+W closes the tab and releases the file handle
- [ ] Dragging one or more files onto the window opens them
- [ ] The recent-files list is saved, capped, and skips missing files with a message
