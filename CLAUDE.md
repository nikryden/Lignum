# Lignum

WPF (.NET 10) read-only log viewer for very large files (2 GB+). Full design and decisions: [docs/design.md](docs/design.md). Read it before making architectural changes.

## Layout
- `Lignum.Core`: indexing, encoding, file reading, search/filter. Must not reference WPF.
- `Lignum.App`: WPF UI (CommunityToolkit.Mvvm) and the custom virtualized viewer control.
- `Lignum.Core.Tests`: xUnit.

## Commands
- Build: `dotnet build`
- Test: `dotnet test`
- Publish: `dotnet publish src/Lignum.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`

## Hard constraints
- Never load a whole file into memory. Everything goes through the line index and positional reads (`RandomAccess`).
- Never write to, or lock, the log file. Open it with `FileShare.ReadWrite | FileShare.Delete`.
- Long-running work (indexing, search) runs in the background and must be cancellable and report progress. The UI thread never blocks on file I/O.
- Regexes always run with a match timeout.
- Benchmarks and multi-GB tests run manually only, not in CI.

## Agent skills

### Issue tracker

Local markdown under `.scratch/<feature>/`; never create GitHub issues. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary, recorded as each issue file's `Status:` line: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
