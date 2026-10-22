---
name: triage-errors
description: Group the errors and warnings in the log, count them and point at the ones that matter
---

Triage the errors in the selected log (or the tab the user names).

1. Call `search` with a regex for error-level lines, e.g. `\b(ERROR|FATAL|CRITICAL|Exception)\b` (isRegex true, maxLines 200). If the log uses other level names, look at a few lines with `read_lines` first and adapt the pattern.
2. Group the matches by message: strip timestamps, ids, numbers, paths and GUIDs so the same error with different values falls into one group.
3. For each group give: a short name, how many lines, the first and last line number, and one example line.
4. Order the groups by importance: fatal/crash first, then errors that repeat or start a burst, then one-off errors. Say which group most likely caused the others (the first error before a burst is often the cause).
5. Do the same briefly for warnings if there are few errors.
6. Bookmark the first line of the top three groups with a short note (`add_bookmark`, color Red), and say you did.

Keep the answer to a table plus three or four sentences. Quote line numbers.
