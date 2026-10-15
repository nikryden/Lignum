# 18: Search/filter history and session restore

**What to build:** The last 20 search terms and filter rules are remembered in dropdowns. When Lignum restarts, it reopens the previous tabs at their scroll positions with their filter rules, re-indexing in the background (the index is never saved to disk). Files that no longer exist are skipped with a message.

**Blocked by:** 12, 16

**Status:** ready-for-agent

- [ ] Search and filter history is capped at 20, most recent first, without duplicates (tested)
- [ ] The session (tabs, scroll positions, filters) is saved on exit and restored on start (tested with a temp settings folder)
- [ ] Missing files in the saved session are skipped with a message
- [ ] Restoring does not block startup; indexing happens in the background
