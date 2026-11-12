# Lignum — Design

Windows desktop log viewer for developers and support staff. Its core purpose is fast, intuitive troubleshooting of very large text logs (2 GB+).

## Goals and constraints

- Opens and searches files over 2 GB smoothly.
- **Read-only.** The app never modifies a log file. User data (bookmarks, settings) is stored separately.
- Intuitive search and filtering are the primary features.
- Personal project. The repo is private on GitHub under the MIT license until it is published.

## Tech stack

- C# on **.NET 10** (LTS), with a **WPF** UI using CommunityToolkit.Mvvm.
- Distributed as a self-contained single-file exe with no installer in v1.
- Lignum's own diagnostics are written by Serilog to a rolling file in `%LOCALAPPDATA%\Lignum\logs`. An unhandled-exception dialog copies the details to the clipboard.

## Solution structure

| Project | Responsibility |
|---|---|
| `Lignum.Core` | Encoding detection, line index, file reading, search/filter engine, line parser abstraction. **No WPF dependency.** |
| `Lignum.App` | WPF UI: virtualized viewer control, tabs, panes, settings, themes. |
| `Lignum.Core.Tests` | xUnit tests, including generated multi-GB test files. Benchmarks run manually. |

## Core

### File access
- Files are opened with `FileShare.ReadWrite | FileShare.Delete`, so logs that a running process is writing or rotating can be read without interfering with it.
- Reads are positional (`RandomAccess.Read` on a `SafeFileHandle`), backed by a small LRU cache of decoded 64 KB blocks. Memory-mapped files are not used because they can't grow, which tail mode needs, and they inflate the reported memory use.

### Encoding
- Supports UTF-8 (with or without BOM), UTF-16 LE/BE, and Windows-1252 as the fallback.
- Detected automatically. The user can override it by clicking the encoding in the status bar.

### Line index
- A background pass records the byte offset where each line starts, as a plain `long` list for simplicity. Offsets are compacted (checkpoint plus relative offsets) only if profiling shows memory is a problem.
- Indexing is incremental: the first screen renders immediately and the scrollbar grows as the pass continues.
- Only `\n` and `\r\n` end a line. A lone `\r` is a normal character.
- The index is append-friendly, so v1.1 tail mode can extend it.
- The index is never saved to disk; it is rebuilt each time the file is opened.

### File change detection
- `FileSystemWatcher` plus a size/timestamp check. When the file changes, the app shows a "File changed: Reload" banner. If the file was truncated or replaced, the index is invalid and must be rebuilt.

### Line parser
- A pluggable interface. v1 treats every line as plain text. Parsing timestamps and levels comes later.

### Search and filter (one engine)
- "Find all" and filtering both produce an ordered **list of line numbers**.
- Plain-text search uses vectorized `Span.IndexOf` on decoded blocks. Regex search uses .NET `Regex` (compiled) with a match timeout, with `NonBacktracking` as an option.
- Searches run in parallel chunks and merge their results in order, with progress reporting and cancellation.
- If the user searches while indexing is still running, results stream in as indexing continues.
- **Filter rules:** a line is shown if it matches *any* include rule and *no* exclude rule. Having no include rules means every line is included. Each rule has text or regex, a case-sensitivity setting and an enabled toggle.

### Performance targets (2 GB, SSD)
- First screen in under 1 s.
- Full index in about 5–10 s.
- Full regex search in about 10–20 s.

## UI

### Viewer control (custom virtualized panel)
- Only the visible lines are drawn (using `GlyphRun` or `FormattedText`). The scrollbar maps directly to line numbers.
- The data source is a list of line numbers plus a line reader. The same control is used for the full view and for the filtered/result view.
- **Why not AvalonEdit or Monaco:** both need the whole document in memory. Feeding them a window of lines breaks scrolling and cross-window selection, and complicates the filter view. Monaco also runs in WebView2 and is limited by the maximum length of a V8 string.
- Selection is character-based (drag, Shift+Click, Ctrl+A) and stored as positions in the file. Large copies are streamed; above about 50 MB the app prompts the user or offers "save selection to file".
- No word wrap in v1; the view scrolls horizontally instead.
- Display is truncated after about 10,000 characters per line, with a "show full line" action. Search still covers the full line.
- Line-number margin, monospaced font (Cascadia Mono, falling back to Consolas; configurable), Ctrl+wheel zoom, tab width 4 (configurable). Font, zoom and tab width are remembered.

### Layout
- **Tabs** with one file per tab. Opening a file that is already open switches to its tab.
- A **split view**: the full log on top and the find/filter results below, with a draggable divider that can be collapsed. Clicking a result scrolls the top view to that line.
- A **detail pane** shows the selected line read-only and word-wrapped, with copy. It is collapsible, docked right by default, and its position is remembered.
- The **status bar** shows line and column, total line count, file size, encoding (click to override), indexing/search progress with cancel, and match count.

### Highlighting
- User-defined color rules color the **whole line** (foreground and/or background). When rules overlap, the first in the list wins.
- Search matches are drawn on top as a separate highlight of the matched text.
- The default preset covers DEBUG, INFO, WARN, ERROR and TRACE. Presets can be saved, and "Factory defaults" brings back the first-run preset.
- **Syntax coloring by file type** (ticket 25): source and data files (`.cs`, `.json`, `.xml`, `.py`…) are colored token by token with a *syntax scheme* chosen by extension or file name. A scheme is ordered regex *token rules*, each with a light and a dark color, bold and italic. About 45 schemes are built in (`Lignum.Core/Syntax/BuiltInSyntaxSchemes.cs`). View > Settings… > Syntax schemes edits, adds, imports and exports them (`syntax-schemes.json`).
  - `.txt`, `.log` and `.lm` never use a scheme, and neither do types without one: they keep the whole-line rules. A file with a scheme does not use the whole-line rules. Search matches, the selection, bookmarks and hand-set line colors still draw on top (a hand-set text color colors the whole line).
  - **Each line is tokenized on its own** (the viewer reads only visible lines, so no lexer state is carried from the start of the file). At each position the earliest match wins, and ties go to the first rule. Block comments and multi-line strings are therefore colored only on the lines with their markers, plus C-style ` * ` doc lines. Regexes have a match timeout, and a rule that times out is skipped for that line.
- **Dark and light themes**, following Windows by default. The user's choice is remembered.

### Context menu (selection)
- Copy
- Copy to search
- Add as include filter
- Add as exclude filter
- Add as highlight rule
- Toggle bookmark
- Show full line

### Keyboard shortcuts
| Keys | Action |
|---|---|
| Ctrl+O / Ctrl+W | Open file / close tab |
| Ctrl+F | Find |
| F3 / Shift+F3 | Next / previous match |
| Ctrl+Shift+C | Copy to search: put the selection (or the word under the cursor; the first line of a multi-line selection) in the find box and run Find all |
| Ctrl+G | Go to line |
| Ctrl+Home / Ctrl+End | Start / end of file |
| Ctrl+F2 | Toggle bookmark |
| F2 / Shift+F2 | Next / previous bookmark |
| Ctrl+C / Ctrl+A | Copy / select all |

### Ways to open files
- Drag and drop, File → Open, recent files, and a command-line argument (`Lignum.exe path.log`).
- **Session restore:** reopens the previous tabs with their scroll positions and filter rules, re-indexing in the background.
- The last 20 search terms and filter rules are remembered in dropdowns.

## Persistence
JSON files under `%APPDATA%\Lignum\`, with no registry use:
- `settings.json`: theme, font, zoom, pane layout, recent files, session, and search/filter history.
- `highlight-presets.json`
- `syntax-schemes.json` (ticket 25)
- `bookmarks/<hash of file path>.json`: designed so that a note field can be added later.

## Automated builds
- GitHub Actions on a Windows runner builds and runs the tests on every push and PR.
- Benchmarks (multi-GB) run **manually only** for now.

## Milestones
1. **Core:** encoding detection and line index, with tests and benchmarks.
2. **Viewer panel:** rendering, scrolling and selection.
3. **Search and filter** engine and the split pane.
4. **Highlight rules** and themes.
5. **Tabs, bookmarks, recent files**, settings and session restore.
6. **Packaging** as a single-file exe.

## Later (not v1)
- **v1.1: tail mode** (follow growing files).
- Bookmark **notes** (the user wants this).
- Parsing timestamps and levels, a query language (`level:error AND user=x`), and a merged timeline across files.
- Pretty-printing JSON/XML in the detail pane.
- Word wrap.
- `.evtx` support.
- An installer (MSIX) with `.log` file association.
