# 02: Open a log and index it

**What to build:** The user opens a log with File → Open (Ctrl+O) or by passing a path on the command line. Lignum opens it read-only without blocking other processes and indexes its lines in the background. The window shows the file name and a line count that rises live until indexing finishes. This introduces the Core `LogDocument` facade: open a path, the line count, indexing progress and completion, cancellation, and an event when lines are added. Only `\n` and `\r\n` end a line. The encoding is UTF-8 only for now.

**Blocked by:** 01

**Status:** resolved

- [x] `LogDocument` opens files with share modes ReadWrite and Delete; tests show another handle can still append to, rename and delete the file while it is open
- [x] Indexing runs off the UI thread; the line count updates while it runs; cancelling stops it
- [x] Line splitting is tested for: `\n`, `\r\n`, a lone `\r` (not a line break), an empty file, a file without a trailing newline, and consecutive empty lines
- [x] The index stores line-start offsets in an append-friendly structure (ready for tail mode later), kept in memory only
- [x] File → Open, Ctrl+O and `Lignum.exe <path>` all open a file
- [x] The window shows the file name and the live line count
- [x] Tests go through `LogDocument` using real temp files

## Comments

`LogDocument.Open(path)` + `StartIndexing()` in Core (`LineIndex` = chunked `long` offsets, internal); `MainViewModel` in App marshals coalesced updates to the UI thread. 30 Core tests (splitting, progress, cancel, sharing) and 5 view-model tests, all on real temp files.
Splitting is asserted via line counts only; line content assertions arrive with ticket 03's line reader.
Smoke-launched `Lignum.exe <path>`: title showed the file name. File → Open dialog and Ctrl+O were not clicked through manually (wired via `ApplicationCommands.Open`, which carries Ctrl+O).
