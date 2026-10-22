---
name: timeline
description: Build a short timeline of what happened in the log (start, key events, errors, stop)
---

Build a timeline of the selected log (or of the time range or topic the user gives).

1. Read the first 30 lines and the last 30 lines (`read_lines`; use `list_tabs` for the line count) to find the time span and the timestamp format.
2. Search for key events: start-up and shutdown (`start|stopp|shut ?down|listening|initiali[sz]`), configuration and version lines, errors and warnings, restarts, connection losses and recoveries, long gaps. Use precise regexes and `maxLines` that fit.
3. Merge the findings into one timeline, oldest first: `time — line N — what happened`. Collapse repeats ("12 × connection refused, 10:02–10:05, lines 400–515").
4. Mark the moment things started to go wrong, and say what came right before it.

At most 25 timeline rows. If the user asked about a topic, keep only the rows that matter for it.
