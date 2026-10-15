# Spec: Lignum v1, a log viewer for very large files

Status: ready-for-agent

## Problem Statement

Developers and support engineers who troubleshoot problems have to read log files that are often hundreds of MB and sometimes larger than 2 GB. Common editors either refuse to open files that size, try to load the whole file into memory and freeze, or lock the file so the service writing it can no longer write or rotate it. Even once a large log is open, finding the relevant lines is slow and awkward. There is no quick way to show only the lines that matter, hide the noise, color errors so they stand out, or jump between related entries while still seeing the lines around them. As a result, troubleshooting takes much longer than it should, and support staff who are less comfortable with command-line tools (grep, tail) are left with poor options.

## Solution

Lignum is a Windows desktop app that opens text logs of any size, including files over 2 GB, almost instantly and never modifies them. It shows the first screen in under a second while it indexes the rest in the background. Users can scroll, select and copy freely, search with plain text or regular expressions, and filter the log with include and exclude rules. Filtered results appear in a split pane below the full log, and clicking a result jumps the full log to that line so its context is visible. Coloring rules make errors and warnings stand out. Bookmarks, tabs, a detail pane for the selected line, a right-click menu ("add as exclude filter", "copy to search") and familiar keyboard shortcuts make the workflow quick and intuitive. Lignum opens files that other programs are still writing without disturbing them, warns when a file changes on disk, and remembers the user's theme, layout, history and open files between sessions.

## User Stories

### Opening files
1. As a support engineer, I want to open a 2 GB+ log file without the app freezing, so that I can troubleshoot production issues from real-world logs.
2. As a developer, I want the first screen of the log to appear in under a second, so that I can start reading immediately.
3. As a user, I want to see indexing progress in the status bar, so that I know how much of the file has been processed.
4. As a user, I want to scroll and read the parts of the file that are already indexed while indexing continues, so that I don't have to wait for the whole file.
5. As a user, I want to cancel indexing, so that I can stop a file I opened by mistake.
6. As a support engineer, I want to open a log that a running service is still writing, so that I can look at live systems without stopping them.
7. As an operations engineer, I want Lignum never to block a service from writing, rotating or deleting its log, so that the viewer never causes a production incident.
8. As a user, I want to open files with File → Open, so that I can browse to a log.
9. As a user, I want to drag and drop files onto the window, so that I can open logs quickly from Explorer.
10. As a user, I want a recent-files list, so that I can reopen logs I looked at recently.
11. As a developer, I want to pass a file path on the command line, so that I can open logs from scripts or other tools.
12. As a user, I want opening a file that is already open to switch to its tab, so that I don't end up with duplicate tabs.
13. As a user, I want several logs open in tabs, so that I can compare logs from different services.
14. As a user, I want to close tabs with Ctrl+W, so that I can tidy up my workspace quickly.

### Encoding
15. As a user, I want the file's encoding detected automatically (UTF-8 with or without BOM, UTF-16 LE/BE, Windows-1252), so that text displays correctly without any configuration.
16. As a Windows administrator, I want UTF-16 logs to display correctly, so that I can read logs written by Windows tools.
17. As a user, I want to see the detected encoding in the status bar and override it with a click, so that I can fix a wrong guess.

### Reading and navigating
18. As a user, I want smooth scrolling across millions of lines, with a scrollbar that maps to the whole file, so that I can move anywhere in the log.
19. As a user, I want line numbers in a margin, so that I can refer to specific lines.
20. As a user, I want Ctrl+G (go to line), so that I can jump to a line number someone reported.
21. As a user, I want Ctrl+Home and Ctrl+End, so that I can jump to the start or end of the log.
22. As a user, I want the status bar to show the current line and column, the total line count and the file size, so that I know where I am.
23. As a user, I want lines that end with `\n` or `\r\n` to split the same way other tools do, so that line numbers match.
24. As a user, I want extremely long lines (over about 10,000 characters) to be truncated on screen with a "show full line" action, so that one huge line cannot freeze the viewer.
25. As a user, I want search and filters to still match the full content of truncated lines, so that truncation never hides results.
26. As a user, I want horizontal scrolling for long lines, so that I can read them without word wrap.
27. As a user, I want a detail pane that shows the selected line in full, word-wrapped, so that I can read long entries comfortably.
28. As a user, I want to collapse the detail pane and dock it right or bottom, with the layout remembered, so that it fits my screen.
29. As a user, I want to copy text from the detail pane, so that I can paste a full entry elsewhere.

### Selecting and copying
30. As a user, I want to select text character by character by dragging, so that I can copy exactly what I need.
31. As a user, I want Shift+Click to extend a selection, so that I can select large ranges, even across millions of lines.
32. As a user, I want Ctrl+A to select the whole file, so that I can copy everything when I need to.
33. As a user, I want Ctrl+C to copy the selection, so that I can paste it into tickets and chats.
34. As a user, I want a warning before copying more than about 50 MB, with an option to save the selection to a file instead, so that I don't overload the clipboard by accident.
35. As a user, I want copying a large selection not to freeze the app, so that I can keep working.

### Searching
36. As a user, I want Ctrl+F to open a find box, so that I can start searching right away.
37. As a user, I want plain-text search with case sensitivity on or off, so that I can find exact strings.
38. As a developer, I want regular expression search, so that I can find patterns like IDs or stack traces.
39. As a user, I want "find all" to list every matching line in the bottom pane, so that I can see all occurrences at once.
40. As a user, I want F3 and Shift+F3 to step through matches, so that I can move between occurrences without the mouse.
41. As a user, I want matched text highlighted in the view, so that I can see exactly where each match is in a line.
42. As a user, I want a match count in the status bar, so that I know how common something is.
43. As a user, I want search progress and a cancel button, so that I can stop long searches.
44. As a user, I want to search a 2 GB file with a regex in about 10–20 seconds, so that searching stays practical.
45. As a user, I want to search before indexing finishes and have results keep arriving, so that I don't have to wait.
46. As a user, I want a bad or catastrophic regex to time out instead of hanging the app, so that one mistake doesn't cost me my session.
47. As a user, I want Ctrl+Shift+C to put the selected text (or the word under the cursor) into the find box and run "find all", so that I can trace an ID through the log with one keystroke.
48. As a user, I want the last 20 search terms remembered in a dropdown, so that I can repeat earlier searches.

### Filtering
49. As a support engineer, I want include filter rules, so that I see only the lines relevant to my problem.
50. As a support engineer, I want exclude filter rules, so that I can hide noisy lines like health checks.
51. As a user, I want a line shown when it matches any include rule and no exclude rule, so that combining rules is predictable.
52. As a user, I want all lines included when I have no include rules, so that exclude-only filtering works.
53. As a user, I want each rule to support plain text or regex and case sensitivity, so that rules are as precise as I need.
54. As a user, I want to toggle individual rules on and off without deleting them, so that I can experiment quickly.
55. As a user, I want filtered results in a split pane below the full log, so that I can see matches and context together.
56. As a user, I want clicking a filtered line to scroll the full log to that line, so that I can see what happened around it.
57. As a user, I want to resize and collapse the split pane, so that I can make room when I need it.
58. As a user, I want filtered lines to show their original line numbers, so that I can refer to them correctly.
59. As a user, I want to select and copy from the filtered pane, so that I can export just the relevant lines.
60. As a user, I want the last 20 filter rules remembered, so that I can reuse common filters.

### Highlighting and themes
61. As a support engineer, I want ERROR, WARN and INFO lines colored by default, so that problems stand out at a glance.
62. As a user, I want to define my own coloring rules (text or regex, foreground and/or background), so that the lines that matter to my system stand out.
63. As a user, I want the first matching rule to win when rules overlap, so that coloring is predictable and I control it by ordering the rules.
64. As a user, I want to save coloring rules as presets, so that I can reuse them for different kinds of logs.
65. As a user, I want search matches drawn on top of rule coloring, so that matches stay visible on colored lines.
66. As a developer, I want a dark theme, so that I can read logs comfortably for long periods.
67. As a user, I want the theme to follow Windows by default and remember my choice, so that the app fits my desktop.
68. As a user, I want coloring presets that look right in both themes, so that switching themes doesn't make text unreadable.
69. As a user, I want to change the monospaced font, so that the text is comfortable to read.
70. As a user, I want Ctrl+mouse wheel zoom that is remembered, so that I can adjust text size quickly.
71. As a user, I want a configurable tab width (default 4), so that indented content lines up.

### Right-click menu
72. As a user, I want "Add as exclude filter" on selected text, so that I can remove noise in two clicks.
73. As a user, I want "Add as include filter" on selected text, so that I can focus on something I've just spotted.
74. As a user, I want "Add as highlight rule" on selected text, so that I can make a pattern stand out instantly.
75. As a user, I want "Copy", "Copy to search", "Toggle bookmark" and "Show full line" in the right-click menu, so that common actions are discoverable without remembering shortcuts.

### Bookmarks
76. As a user, I want to bookmark lines with Ctrl+F2, so that I can mark important events while I investigate.
77. As a user, I want F2 and Shift+F2 to jump between bookmarks, so that I can move between my marked points quickly.
78. As a user, I want bookmarks shown in the margin, so that I can see marked lines while scrolling.
79. As a user, I want bookmarks saved per file and restored when I reopen it, so that my investigation survives a restart.

### File changes on disk
80. As a user, I want a "File changed: Reload" banner when the open file grows, shrinks or is replaced, so that I know my view is stale.
81. As a user, I want a truncated or rotated file detected, so that I never see wrong content from an outdated index.

### Session and settings
82. As a user, I want my open tabs, scroll positions and filter rules restored when I restart, so that I can continue where I left off.
83. As a user, I want settings stored as plain files in my profile, not in the registry, so that I can back them up or move them to another machine.
84. As a user, I want Lignum to be a single exe with no installer, so that I can copy it to servers and jump boxes.

### Reliability and diagnostics
85. As a user, I want the UI to stay responsive during indexing, searching and copying, so that the app never appears frozen.
86. As a user, I want a friendly error dialog that copies the crash details, so that I can report problems easily.
87. As the maintainer, I want Lignum to write its own diagnostic log, so that I can investigate bug reports.
88. As the maintainer, I want benchmarks I can run on demand against generated multi-GB files, so that I can check the performance targets are still met.

## Implementation Decisions

### Platform and packaging
- C# on .NET 10 (LTS). The UI is WPF using CommunityToolkit.Mvvm.
- Shipped as a self-contained, single-file win-x64 exe. No installer and no file association in v1.
- Lignum's own diagnostics are written by Serilog to a rolling log in the local app data folder. A global unhandled-exception handler shows a dialog that copies the details.

### Modules
- **Core library** (no WPF reference): file access, encoding detection, line index, line reading, search/filter engine, line parser abstraction, change detection.
- **App** (WPF): the custom viewer control, view models, tabs and panes, themes, settings and session persistence.
- **Core tests** (xUnit).

### Core: the `LogDocument` facade (the main seam)
This is a single public entry point for one open log file. Its responsibilities:
- **Open** a path: the file is opened read-only with share modes ReadWrite and Delete, so it never blocks writers or rotation.
- **Indexing state:** the current line count, progress, completion, and cancellation. It raises an event as lines are added.
- **Encoding:** the detected encoding and a way to override it, which triggers a re-index.
- **Read lines:** returns the text for a range of lines, and the full text of a single line regardless of length.
- **Read text range:** streams the text between two (line, column) positions, used for copying large selections.
- **Query:** takes a query (a list of include and exclude rules, each with text or regex, a case setting and an enabled flag, or a single find pattern) and returns a *result set*: an ordered, growing list of matching line numbers, with progress and cancellation.
- **Change notification:** reports when the file has grown, been truncated or been replaced.

Other internal decisions:
- **File access:** positional reads (`RandomAccess`) through a small LRU cache of decoded 64 KB blocks. No memory-mapped files.
- **Line index:** built by one sequential pass in the background, storing each line's start offset as a plain list of 64-bit values. It is append-friendly for future tail mode, and it is never saved to disk. Only `\n` and `\r\n` end a line. Compacting the offsets is deferred unless profiling shows it is needed.
- **Encoding detection:** BOM sniffing first, then a UTF-8 validity heuristic, then UTF-16 detection from the pattern of zero bytes, with Windows-1252 as the fallback. The line splitter must handle UTF-16 newlines (two-byte code units).
- **Search engine:** plain text uses vectorized span `IndexOf` on decoded blocks. Regex uses compiled `Regex` with a match timeout, and `NonBacktracking` is a user option. Searches run in parallel chunks whose results are merged in order. A query running during indexing processes lines as they are indexed.
- **Filter rule combination:** a line is included if (there are no enabled include rules, or it matches at least one enabled include rule) and it matches no enabled exclude rule.
- **Line parser:** a pluggable interface. v1 ships only a plain-text parser. Parsing timestamps and levels plugs in here later.
- **Change detection:** `FileSystemWatcher` plus a size/last-write check. A file that has become smaller than the indexed length counts as truncated or replaced, and the index is invalid.

### App
- **Viewer control:** a custom virtualized WPF element that draws only the visible rows (using GlyphRun or FormattedText). Its data source is a *line source*: a list of line numbers (all lines, or a result set) plus the document's line reader. The same control is used for the main view and the results pane. AvalonEdit and Monaco were rejected because both want the whole document in memory, and feeding them a window of lines breaks scrolling, selection across windows, and the filter view.
- **Selection:** stored as file positions (line, column), independent of what is on screen. Copying streams the text through the Core text-range reader. Above about 50 MB the app prompts the user or offers to save to a file.
- **Display truncation** at about 10,000 characters per line. No word wrap; horizontal scrolling instead.
- **Layout per tab:** the main view on top, the results pane (find all / filter, one concept) below with a draggable, collapsible divider, and a collapsible detail pane docked right by default.
- **Highlighting:** an ordered list of whole-line rules (foreground and/or background), where the first match wins, plus a separate overlay for search matches. Presets are saved, and the default preset covers ERROR, WARN and INFO, with colors for both themes.
- **Themes:** light and dark, following the Windows setting by default. The user's choice is saved.
- **Pixel-to-position mapping** (pixels to line and column, and the reverse) is a pure helper with no rendering dependency.
- **Commands and shortcuts:**

  | Keys | Action |
  |---|---|
  | Ctrl+O | Open file |
  | Ctrl+W | Close tab |
  | Ctrl+F | Find |
  | F3 / Shift+F3 | Next / previous match |
  | Ctrl+Shift+C | Copy to search (selection, or the word under the cursor, or the first line of a multi-line selection; then run "find all") |
  | Ctrl+G | Go to line |
  | Ctrl+Home / Ctrl+End | Start / end of file |
  | Ctrl+F2 | Toggle bookmark |
  | F2 / Shift+F2 | Next / previous bookmark |
  | Ctrl+C | Copy |
  | Ctrl+A | Select all |

- **Right-click menu:** Copy, Copy to search, Add as include filter, Add as exclude filter, Add as highlight rule, Toggle bookmark, Show full line.
- **Persistence:** JSON under the roaming app data folder: settings (theme, font, zoom, tab width, pane layout, recent files, session, search and filter history), highlight presets, and per-file bookmarks keyed by a hash of the file path. The bookmark schema leaves room for a future note field. Nothing is stored in the registry. The settings location can be injected so tests can use a temp folder.
- **Session restore:** reopens tabs, scroll positions and filter rules, and re-indexes in the background.

### Performance targets (2 GB file on an SSD)
- First screen in under 1 s.
- Full index in about 5–10 s.
- Full regex search in about 10–20 s.
- The UI thread never blocks on file I/O.

## Testing Decisions

- **What a good test is:** it checks behavior that callers can observe through the public seam: inputs, then outputs and events. It does not depend on internals such as how the index is stored, the cache size or the chunking strategy, so those can be rewritten freely. Tests use real files written to a temp folder with known content, not mocks of the file system.
- **Seam 1: Core `LogDocument`** (the main seam; nearly all logic is tested here):
  - Encoding detection for each supported encoding, with and without BOM, and the manual override.
  - Line splitting with `\n`, `\r\n`, a lone `\r`, an empty file, no trailing newline, and a very long line.
  - Reading lines and text ranges, including ranges across many lines and the full text of a truncated line.
  - Lines become available while indexing runs; cancelling indexing; progress reports.
  - Plain-text and regex search, case sensitivity, include/exclude combination, disabled rules, no include rules.
  - Regex timeout behavior; cancelling a search; a query started while indexing is still running returns the same results as one run afterwards.
  - Opening a file while another handle is writing to it, and that handle can still write, rename and delete the file.
  - Change notifications for a file that grows, is truncated or is replaced.
- **Seam 2: App view models** (no UI; the settings folder points at a temp directory):
  - Opening a file that is already open switches to its tab.
  - Selection model and copying through the line reader; the large-copy threshold prompt.
  - Copy to search, choosing the selection, the word under the cursor or the first line of a multi-line selection.
  - Adding include/exclude/highlight rules from a selection.
  - Highlight rule resolution: first match wins.
  - Bookmark toggling, navigation, and saving and restoring them.
  - Settings, theme, history (capped at 20) and session restore.
  - The pixel-to-position helper.
- **Not tested automatically:** the drawing done by the viewer control. It is checked manually and with the benchmarks.
- **Benchmarks:** a separate project that generates multi-GB files and measures the three performance targets. It is run **manually only**, never in CI.
- **CI:** GitHub Actions on a Windows runner builds the solution and runs the Core and App tests on every push and PR.
- **Prior art:** none yet; this is a new codebase. The first Core tests set the pattern: temp-file fixtures plus generated content builders.

## Out of Scope

- Tail mode (following files as they grow): planned for v1.1. The index and change detection are designed to support it.
- Notes attached to bookmarks: planned for a later release, and wanted by the owner.
- Parsing timestamps and levels, a query language (`level:error AND user=x`), and a merged timeline across files.
- Pretty-printing JSON or XML in the detail pane.
- Word wrap in the main view.
- Windows Event Log (`.evtx`) and other non-text formats.
- Editing or saving log files. Lignum is strictly read-only.
- An installer, MSIX packaging, `.log` file association.
- Cross-platform support (although Core stays free of WPF so Avalonia remains possible).
- Saving the index to disk.

## Further Notes

- The full design rationale is in `docs/design.md`, and the hard constraints are in `CLAUDE.md`.
- Suggested build order (riskiest first):
  1. Core encoding and index, with benchmarks.
  2. Viewer control.
  3. Search, filter and the split pane.
  4. Highlighting and themes.
  5. Tabs, bookmarks and settings.
  6. Packaging.
- The repo is private under the MIT license until the owner decides to publish.
