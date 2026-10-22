---
name: explain-exception
description: Explain an exception or stack trace in the log - what failed, where, and why it probably happened
---

Explain an exception in the log.

1. Find it: the user's selected text (`get_selection`), a line they name, or else the first exception (`search` for `Exception|Traceback|panic:|at [\w.$]+\(` with isRegex true).
2. Read the whole stack trace and about 20 lines before it with `read_lines` (stack traces can be long; read on until the trace ends). Use `show_full_line` if a line is cut.
3. Explain in plain words: the exception type and message, the innermost cause (the last "Caused by" / inner exception), and the first frame in the application's own code (skip framework frames).
4. Use what happened just before it in the log (requests, ids, config values) to say why it most likely happened. Say clearly what is certain and what is a guess.
5. Suggest what to check or change. If the user has the source code, offer to look at the frame's file with `read_file` (Lignum asks them first).
6. Count how often the same exception occurs (`search`) and say so.
