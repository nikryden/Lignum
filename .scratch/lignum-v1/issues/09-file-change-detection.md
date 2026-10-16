# 09: File change detection

**What to build:** When the open file grows, shrinks or is replaced on disk, a banner says "File changed: Reload". If the file was truncated or replaced, the current index is treated as invalid. Reloading re-indexes the file. `LogDocument` exposes change notifications based on `FileSystemWatcher` plus a size/last-write check. This lays the groundwork for tail mode in v1.1.

**Blocked by:** 02

**Status:** resolved

- [x] `LogDocument` raises distinct notifications for grown and truncated/replaced (tested with real temp files)
- [x] The banner appears for both cases, and Reload rebuilds the index
- [x] A truncated file never shows content from the stale index
- [x] Detection works while another process has the file open for writing

## Comments

`LogDocument` raises `FileChanged` (Grown / Truncated / Replaced) from a FileSystemWatcher plus a 1 s poll of its own handle's length and a file-identity check (volume + file index) of the path, so rotation is caught even when the new file is larger. Truncated/Replaced set `IsIndexValid = false`, make `LineCount` 0, cancel indexing and stop watching; Grown repeats per growth for tail mode. `MainViewModel` shows the `FileChangedBanner` and `ReloadCommand` reopens the path. Banner visuals were not checked by hand (only build + view-model tests). Ticket 03's line reader must also refuse reads when `IsIndexValid` is false.
