# 02: Search files in a folder

**What to build:** Search files that are not open: pick a folder, optionally include subfolders, and filter by file type (for example `*.log;*.txt`). The results list which files match and the matching lines. Opening a result opens that file in a tab at the matching line.

**Blocked by:** 01

**Status:** done

- [x] Choose a folder, a "subfolders" option and a file-type filter
- [x] Files are searched one at a time in the background, never fully loaded into memory, and opened with the same share modes as normal (CLAUDE.md hard constraints)
- [x] Progress (files done of total) and cancel work
- [x] Results group the matches by file; opening one goes to the line

## Open questions
- Is there a size or file-count limit, or a warning before searching a huge folder?
- Should it skip binary files automatically?

## Decisions (2026-10-17)
- Big folders: ask before searching when there are over 1,000 files or over 10 GB in total.
- Binary files are skipped automatically (a null byte in the first 8 KB).
- UI: a fourth page in the Find window, "Find in files". Results go in the shared list used by find in all tabs (grouped by file, collapsible, highlighted). Clicking a match opens the file in a tab at that line.
- No per-tab mode: folder results always use the shared list.
