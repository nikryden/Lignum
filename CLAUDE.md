# Lignum

WPF (.NET 10) read-only log viewer for very large files (2 GB+). Full design and decisions: [docs/design.md](docs/design.md). Read it before making architectural changes.

## Layout
Solution `Lignum.sln` at the repo root.
- `src/Lignum.Core` (net10.0): indexing, encoding, file reading, search/filter. Must not reference WPF.
- `src/Lignum.App` (net10.0-windows): WPF UI (CommunityToolkit.Mvvm) and the custom virtualized viewer control. Builds `Lignum.exe`.
- `tests/Lignum.Core.Tests` (net10.0): xUnit tests for Core.
- `tests/Lignum.App.Tests` (net10.0-windows): xUnit tests for App view models and diagnostics.

## Commands
- Build: `dotnet build`
- Test: `dotnet test` (CI runs `dotnet test --filter "Category!=Manual"`; tag benchmarks and multi-GB tests with `[Trait("Category", "Manual")]`)
- Publish: `dotnet publish src/Lignum.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` (output: one `Lignum.exe` in `src/Lignum.App/bin/Release/net10.0-windows/win-x64/publish/`; add `-p:Version=X.Y.Z` to stamp a version). CI release build: manual `Publish` workflow (`.github/workflows/publish.yml`).

## Hard constraints
- Never load a whole file into memory. Everything goes through the line index and positional reads (`RandomAccess`).
- Never write to, or lock, the log file. Open it with `FileShare.ReadWrite | FileShare.Delete`.
- Long-running work (indexing, search) runs in the background and must be cancellable and report progress. The UI thread never blocks on file I/O.
- Regexes always run with a match timeout.
- Benchmarks and multi-GB tests run manually only, not in CI (`[Trait("Category", "Manual")]`).

## Agent skills

### Issue tracker

Local markdown under `.scratch/<feature>/`; never create GitHub issues. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary, recorded as each issue file's `Status:` line: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
