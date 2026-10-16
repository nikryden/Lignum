# 01: Solution scaffold, CI and diagnostics

**What to build:** A .NET 10 solution with the Core library (no WPF reference), the WPF App (CommunityToolkit.Mvvm) and the Core tests (xUnit). Launching the App shows an empty main window titled "Lignum". Lignum writes its own diagnostic log with Serilog to a rolling file in the local app data folder. Any unhandled exception shows a friendly dialog that copies the details to the clipboard. GitHub Actions builds the solution and runs the tests on a Windows runner on every push and PR.

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] The solution builds with `dotnet build` and tests pass with `dotnet test`
- [x] The Core project has no reference to WPF or any Windows desktop framework
- [x] The App starts and shows an empty main window
- [x] Serilog writes a rolling log file under the local app data `Lignum\logs` folder on startup
- [x] An unhandled exception (UI thread and background tasks) shows the error dialog, which copies the details to the clipboard, and is logged
- [x] A GitHub Actions workflow builds and tests on a Windows runner for pushes and PRs
- [x] `CLAUDE.md` build/test/publish commands match the actual project layout

## Comments

Scaffolded `Lignum.sln` with src/Lignum.Core, src/Lignum.App, tests/Lignum.Core.Tests and tests/Lignum.App.Tests; 7 tests pass (Core architecture guard, DiagnosticLog, CrashHandler). App launch verified manually (window titled "Lignum", `%LOCALAPPDATA%\Lignum\logs\lignum-YYYYMMDD.log` written); publish command verified.
Crash logging/report content is unit-tested through `CrashHandler` with a fake `ICrashReporter`; the real `ClipboardCrashReporter` dialog and the event wiring in `App.xaml.cs` were not exercised by forcing a crash (manual check). The CI workflow was not run on GitHub (no push from here).
