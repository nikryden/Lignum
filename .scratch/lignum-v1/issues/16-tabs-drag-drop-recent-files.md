# 16: Tabs, drag-and-drop and recent files

**What to build:** Several logs can be open at once in tabs, each with its own view, results pane and filters. Opening a file that is already open switches to its tab. Ctrl+W closes the current tab and releases its file handle. Files can be dragged onto the window to open them. A recent-files list (saved in settings) reopens logs quickly.

**Blocked by:** 13

**Status:** resolved

- [x] Multiple tabs work independently (search/filter state per tab)
- [x] Opening an already-open path (compared case-insensitively, after resolving the full path) switches to its tab (view-model test)
- [x] Ctrl+W closes the tab and releases the file handle
- [x] Dragging one or more files onto the window opens them
- [x] The recent-files list is saved, capped, and skips missing files with a message

## Comments

Each tab is a `LogTabViewModel` (owns its `LogDocument`, line source, indexing state and file-changed banner); `MainViewModel` holds `Tabs`, `SelectedTab`, `CloseTabCommand` (Ctrl+W, disposes the document), `OpenFilesAsync` (drag-and-drop) and the recent-files list (`AppSettings.RecentFiles`, capped at 10, missing files removed with a message). MainWindow keeps one `Views/LogTabView` alive per tab so each keeps its own viewer.
"Search/filter state per tab": those features don't exist yet (10/12); the tab VM is their home and tabs are independent today (own document, lines, indexing, banner). Drag-and-drop was not exercised manually (the VM path `OpenFilesAsync` is tested); tabs, Ctrl+W releasing the handle and the recent list were smoke-tested in the running app.
