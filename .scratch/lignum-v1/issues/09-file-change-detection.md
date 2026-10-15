# 09: File change detection

**What to build:** When the open file grows, shrinks or is replaced on disk, a banner says "File changed: Reload". If the file was truncated or replaced, the current index is treated as invalid. Reloading re-indexes the file. `LogDocument` exposes change notifications based on `FileSystemWatcher` plus a size/last-write check. This lays the groundwork for tail mode in v1.1.

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] `LogDocument` raises distinct notifications for grown and truncated/replaced (tested with real temp files)
- [ ] The banner appears for both cases, and Reload rebuilds the index
- [ ] A truncated file never shows content from the stale index
- [ ] Detection works while another process has the file open for writing
