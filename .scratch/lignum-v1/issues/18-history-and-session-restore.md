# 18: Search/filter history and session restore

**What to build:** The last 20 search terms and filter rules are remembered in dropdowns. When Lignum restarts, it reopens the previous tabs at their scroll positions with their filter rules, re-indexing in the background (the index is never saved to disk). Files that no longer exist are skipped with a message.

**Blocked by:** 12, 16

**Status:** resolved

- [x] Search and filter history is capped at 20, most recent first, without duplicates (tested)
- [x] The session (tabs, scroll positions, filters) is saved on exit and restored on start (tested with a temp settings folder)
- [x] Missing files in the saved session are skipped with a message
- [x] Restoring does not block startup; indexing happens in the background

## Comments

`SearchHistory` (app-wide, on `MainViewModel.History`, saved in settings.json) records find-all terms and added filter rule texts; the find box and filter add box are now editable ComboBoxes listing them. `MainViewModel.SaveSession()` (called when the window closes) / `RestoreSession()` (called after the window shows) keep tabs, selected tab, first visible line and filter rules in `AppSettings.Session`; missing files are skipped with an `ErrorMessage`, indexing runs in the background, recent files are untouched by a restore. Tests: `HistoryTests`, `SessionTests` (temp settings folder).
Not verified manually (the App has no settings-folder override, so no manual run against the real profile): the dropdown look/behaviour and the view applying the restored scroll position (`LogTabView.Scroll.cs`) once enough lines are indexed.
