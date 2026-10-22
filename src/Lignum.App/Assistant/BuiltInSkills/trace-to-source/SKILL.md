---
name: trace-to-source
description: Find the source code that wrote a log line or threw an exception, in a repository the user points to
---

Trace a log line to the code that produced it.

1. Take the line the user selected (`get_selection`) or names, and read it with `read_lines`. Pick the constant part of the message (drop timestamps, ids and values) and, for a stack trace, the first frame in the application's code (file and line, or class and method).
2. Ask the user for the repository folder if they did not give one. Lignum asks them before you can read it.
3. Look around with `list_dir`. If `run_command` is allowed, `git grep -n "<constant text>"` (shell none, cwd = the repository) is the fastest way to find the line; otherwise list folders and read the likely files with `read_file`.
4. Read the code around the match (about 40 lines). Explain when this line is logged or the exception thrown, which values end up in the message, and what the log tells about the state at that point.
5. If a fix is obvious, describe it. Only change files with `edit_file` when the user asks you to.
