# 27: Auto update a tab when its file changes

**What to build:** Each tab can be set to update by itself when its file changes on disk, instead of showing the "File changed: Reload" banner. When a file grows, only the new bytes are indexed, so a 2 GB log keeps up without a full re-index. This is the "tail mode" that `docs/design.md` set aside for v1.1.

**Blocked by:** 09 (file change detection, lignum-v1)

**Status:** ready-for-agent

## Behaviour

- The tab's right-click menu has an **Auto update** check item. It is off by default.
- Under it is a choice for what happens on an update: **Jump to last line** or **Stay in place**. Stay in place keeps the view and selection where they are. The exception is when the view already shows the last line: then it follows the new lines, like `tail -f`.
- The choice is remembered per file path. A tab restored with the session, or the same file opened again later, gets the same Auto update and on-update settings.
- **Grown:** with Auto update on, the index is extended from the last indexed offset. The line count, scrollbar and status bar grow, and the viewer shows the new lines. No banner appears. With Auto update off, the banner works as it does today.
- **Truncated or replaced:** with Auto update on, the tab reloads by itself (a full re-index, as Reload does). With Auto update off, the banner works as it does today.
- **Find and filter results** are not extended to the new lines. When the file has grown since a search ran, the results pane says the results are outdated and can be run again.
- **Tab icon:** a small icon at the top of the tab, shown on every tab, not only the selected one:
  - "Needs update": the file changed and Auto update is off (same condition as the banner).
  - "Auto updating": Auto update is on.
  - No icon: Auto update is off and nothing has changed.
- Hard constraints still hold: the file is never written to or locked, indexing the new bytes runs in the background and can be cancelled, and the UI thread never blocks on file I/O.

## Done when

- [x] `LogDocument` can extend its index after the file grows, without re-reading the indexed part (tested with real temp files, including a last line that had no line ending yet and gets finished by the growth)
- [x] With Auto update on, growth extends the tab and no banner shows; with it off, the banner shows as before
- [x] With Auto update on, truncation or replacement reloads the tab by itself, and no stale lines are shown
- [x] Jump to last line moves to the end on each update. Stay in place keeps the position, but follows the new lines when the last line was in view
- [x] Find/filter results show as outdated after growth
- [x] The tab icon shows "needs update" or "auto updating" on every tab that needs one
- [ ] The settings are remembered per file path across reopen and session restore
- [ ] View-model tests for the toggle, both on-update modes, the icon state and the remembered settings

## Comments

Step 2 (Core): `LogDocument.IndexAppendedAsync()` indexes from `BytesIndexed` to the file's current length, keeping whether the next byte starts a line, so an unfinished last line grows in place and a CRLF split by the growth ends the line once. It waits for the indexing pass, queues calls one after another, raises `LinesAdded`, raises `TotalBytes`, and does nothing once the index is invalid or the document is disposed. A stray odd byte at the end of a UTF-16 file is indexed again together with the byte that completes it. Tests: `GrowthTests`.

Step 3 (view model): `LogTabViewModel.AutoUpdate` and `UpdatePosition` (StayInPlace, JumpToEnd) in `LogTabViewModel.AutoUpdate.cs`. A change goes through `Update()`: growth calls `IndexAppendedAsync` on the same document, and truncation or replacement reopens the file. Reload in the banner uses `Update()` too, so a grown file is no longer re-indexed from the start. Turning Auto update on applies a pending change. Following uses the viewport rows the view reports (`ReportViewportRows`) and `ScrollToEndRequested`; JumpToEnd runs `GoToEnd`. A reopen that fails puts the banner back. Tests: `AutoUpdateTests`. The menu items, tab icon, outdated results and remembered settings are still to do.

Step 4 (UI): the tab's right-click menu has Auto update and two radio-style check items, "On update: jump to last line" and "On update: stay in place" (`JumpsToEnd`/`StaysInPlace`). `LogTabViewModel.Indicator` (None, NeedsUpdate, AutoUpdating) drives a Segoe MDL2 glyph at the top left of every tab, using the new `TabNeedsUpdateBrush` and `TabAutoUpdateBrush` in both themes. `SearchViewModel.IsOutdated` is set when growth adds lines, and the results pane shows an "Outdated: run again" button (`RunAgainCommand`). Not yet checked by hand in the running app.

Fix after a manual test: rows looked shifted (the end of one line drawn over the next) when the file was rewritten in place and got longer. Growth then looked like an append. `IndexAppendedAsync` now first checks that the last 4 KB indexed are unchanged on disk (read directly, not through the block cache). If they changed, it reports `Replaced`, and the tab reloads. Test: `A_file_rewritten_in_place_and_longer_is_not_taken_as_an_append`.
