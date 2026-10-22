---
name: compare-runs
description: Compare two open logs (e.g. a good run and a bad run) and show what differs
---

Compare two runs. Use the two tabs the user names; with exactly two tabs open use those; otherwise ask which (`list_tabs`).

1. For each tab: read the start (first 40 lines) for version, configuration and environment lines, and note the line count and time span.
2. Count errors and warnings in each (`search` per tab with the same pattern). Use `search_all_tabs` for patterns you want side by side.
3. Find messages that occur in one run but not the other: search each tab for the distinctive error and warning messages of the other.
4. Compare timing of the same milestones (start-up done, first request, shutdown) if both logs have timestamps.

Answer with: what is the same, what differs (config/version first, then errors, then timing), and the most likely reason the runs behaved differently. Quote tab names and line numbers.
