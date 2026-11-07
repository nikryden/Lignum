# Lignum

**A fast, read-only log viewer for Windows that opens, searches and filters files of 2 GB and more.**

Lignum is built for developers and support staff who need to dig through very large text logs. The first screen appears at once, the line index builds in the background, and search, filter and highlighting stay responsive. Lignum never writes to a log file and never locks it, so you can read logs that a running process is still writing or rotating.

Home page: [`site/index.html`](site/index.html)

## Features

- **Very large files.** Never loads a whole file into memory. Lines are read on demand through a background line index. Target on an SSD: first screen in under 1 s, a 2 GB file fully indexed in about 5–10 s.
- **Search.** Plain text or regex (with a match timeout), case-sensitive or not, run in parallel with progress and cancel. Find in all open tabs (Ctrl+Shift+F) or in the files of a folder.
- **Filter.** Include and exclude rules: a line shows if it matches any include rule and no exclude rule. Hide non-matching lines, search inside the filter, and keep a history of terms and rules.
- **Split view.** The full log on top and the results below. Click a result to jump to it.
- **Highlight rules.** Whole-line colors for the light and dark themes, with color pickers and presets. The defaults cover DEBUG, INFO, WARN, ERROR and TRACE, and "Factory defaults" brings them back.
- **Syntax coloring by file type.** About 45 built-in schemes, including C#, C/C++, Java, Python, JavaScript/TypeScript, Go, Rust, SQL, JSON, XML/XAML, YAML, PowerShell, Dockerfile and Markdown. Add or edit schemes, and import or export them as JSON. `.txt`, `.log` and `.lm` files always use the highlight rules.
- **Bookmarks with notes.** Five colors, a bookmarks pane with sorting and filtering, notes with links, and bulk actions from the margin.
- **`.lm` packages.** Export a log together with its bookmarks and notes, and send it to a colleague.
- **Tabs and sessions.** One file per tab. Open a whole folder (Ctrl+Shift+O). Recent files. Previous tabs, scroll positions and filters come back on the next start.
- **Comfort.** A detail pane for the selected line, light and dark themes that follow Windows, zoom, font and tab width, encoding detection with a manual override (UTF-8, UTF-16 LE/BE, Windows-1252), a "file changed: reload" banner, and "Open in editor".
- **AI access (optional, off by default).** Lignum is an MCP server (`Lignum.exe --mcp`), so GitHub Copilot, Claude Desktop, Claude Code, Cursor, Codex and others can search your open logs, add filters, highlight rules and bookmarks. The AI runs in your own tool and Lignum needs no API key. Every call is listed and controlled by permissions. See [docs/ai-access.md](docs/ai-access.md).

## Install

Download from [GitHub Releases](https://github.com/nikryden/Lignum/releases):

- **`Lignum-<version>.msi`**: installs Lignum, adds it to the Start menu and can associate `.log` and `.lm` files with it.
- **`Lignum.exe`**: a portable, self-contained single file. No .NET installation is needed.

Requires Windows 10 or 11 (x64).

## Keyboard shortcuts

| Keys | Action |
|---|---|
| Ctrl+O / Ctrl+Shift+O | Open file / open folder |
| Ctrl+W | Close tab |
| Ctrl+F / Ctrl+Shift+F | Find / find in all tabs |
| F3 / Shift+F3 | Next / previous match |
| Ctrl+Shift+C | Copy the selection to search and find all |
| Ctrl+G | Go to line |
| Ctrl+Home / Ctrl+End | Start / end of file |
| Ctrl+F2 | Toggle bookmark |
| F2 / Shift+F2 | Next / previous bookmark |
| Ctrl+C / Ctrl+A | Copy / select all |
| Ctrl+Plus / Ctrl+Minus / Ctrl+0 | Zoom in / out / reset (Ctrl+wheel also zooms) |
| Ctrl+Shift+E | Open in external editor |
| Ctrl+Shift+A | AI access panel |

## Settings

Settings are JSON files in `%APPDATA%\Lignum\` (`settings.json`, `highlight-presets.json`, `syntax-schemes.json`, `bookmarks\`). Lignum does not use the registry for settings. Its own diagnostic log is in `%LOCALAPPDATA%\Lignum\logs`.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
dotnet build
dotnet test --filter "Category!=Manual"

# Single-file exe in src/Lignum.App/bin/Release/net10.0-windows/win-x64/publish/
dotnet publish src/Lignum.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# MSI installer (after publishing) in artifacts/
dotnet run -c Release --project installer/Lignum.Installer -- -o artifacts
```

Benchmarks generate a multi-GB log and run manually:

```powershell
dotnet run -c Release --project benchmarks/Lignum.Benchmarks -- --size 2GB
```

Releases are built by the `Publish` workflow when a version tag (`v0.2.0`) is pushed.

## Project layout

| Path | Contents |
|---|---|
| `src/Lignum.Core` | Line index, encoding detection, file reading, search and filter, syntax tokenizer. No WPF. |
| `src/Lignum.App` | WPF UI (CommunityToolkit.Mvvm), the virtualized viewer control and the MCP server. Builds `Lignum.exe`. |
| `tests/` | xUnit tests for Core and App. |
| `benchmarks/` | The manual performance benchmark. |
| `installer/` | The MSI builder. |
| `site/` | The home page (static HTML and CSS). |
| `docs/` | [Design](docs/design.md), [AI access](docs/ai-access.md) and [decision records](docs/adr/). |

## License

[MIT](LICENSE) © 2026 Niklas Rydén
