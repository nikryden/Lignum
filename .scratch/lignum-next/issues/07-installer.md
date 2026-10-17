# 07: Installer

**What to build:** A Windows installer for Lignum, built with **FalkForge**. It installs the published single-file `Lignum.exe`, adds Start-menu shortcuts and an uninstaller, and registers file associations. The no-installer single exe (v1 ticket 20) stays available as a portable download.

**Reference docs (read first):**
- https://github.com/Falkesand/FalkForge
- https://falkesand.github.io/FalkForge/

**Blocked by:** 03 (the app icon is used by the installer and the file associations)

**Status:** in progress (installer, single instance, workflow and ADR done; manual install test and the update check remain)

- [x] The installer is built with FalkForge from the output of the publish command in CLAUDE.md
- [x] Installs per user, without needing admin, unless the FalkForge docs recommend otherwise
- [x] Start-menu shortcut with the app icon; an uninstaller that leaves the user's settings in `%APPDATA%\Lignum` unless they choose to remove them
- [x] Optional file associations: `.log`, and `.lm` packages (ticket 06). Double-clicking opens the file in Lignum, reusing the running instance if one is open
- [x] Upgrading over an older version keeps settings, bookmarks and the session
- [x] The manual GitHub workflow (`publish.yml`) also produces the installer as an artifact
- [ ] Install, upgrade and uninstall are each tested once on a clean machine (manual check, e.g. Windows Sandbox)

## Open questions
- Per-user or per-machine install?
- Is code signing needed (to avoid SmartScreen warnings), and is there a certificate?
- Should `.log` association be on by default, or opt-in during install?
- Is automatic update checking wanted, if FalkForge supports it?
- "Reuse the running instance" needs single-instance support in the app; is that wanted, or should each double-click open a new window?

## Notes
- This is out of scope for v1 ("An installer, MSIX packaging, `.log` file association" in the v1 spec), so this ticket also reverses that decision. Record the decision as an ADR in `docs/adr/` when it is accepted.

## Decisions (2026-10-17)
- Install scope: the user chooses during install; the default is the current user only (no admin rights).
- Code signing: no certificate for now; SmartScreen warnings are a known later problem.
- `.log` association: a checkbox in the installer, off by default. `.lm` packages always open in Lignum.
- Update checks: wanted, but low priority (do last, or as a follow-up).
- Single instance: double-clicking a file while Lignum is running opens it as a new tab in the running window.
