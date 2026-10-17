# 1. Ship an installer next to the portable exe

Date: 2026-10-17. Status: accepted.

## Context

The v1 spec left an installer, MSIX packaging and the `.log` file association out of scope: Lignum shipped as one self-contained `Lignum.exe`. Users now want Start-menu shortcuts, a clean uninstall, and double-click to open `.lm` packages (ticket 06) and optionally `.log` files.

## Decision

- Build a Windows Installer package (MSI) with **FalkForge** (C# fluent API, NuGet `FalkForge` 0.5.0-beta), in `installer/Lignum.Installer`. It is not in `Lignum.sln`, because it needs the published exe; the manual Publish workflow builds it after the exe.
- **Dual-purpose package:** per user by default (`ALLUSERS=2`, `MSIINSTALLPERUSER=1`, installed under `%LocalAppData%\Programs`, no admin rights). The Advanced dialog set lets the user choose all users instead.
- **Features:** the app, its Start-menu shortcut and the `.lm` association are always installed. The `.log` association is a separate feature that is off by default.
- **Upgrades:** a fixed UpgradeCode and a major upgrade replace older versions. Settings, bookmarks and the session live in `%APPDATA%\Lignum`, which the installer never touches, so they survive upgrades and uninstalls.
- **Single instance:** a double-clicked file opens as a new tab in a running Lignum (named mutex plus a named pipe, per user and settings folder).
- The portable single exe stays available as its own download.

## Consequences

- No code-signing certificate yet: SmartScreen warns when the installer is downloaded. This is a known later problem.
- FalkForge is a beta and lightly documented. If it breaks or is abandoned, the installer project is small enough to move to WiX.
- Install, upgrade and uninstall need a manual check on a clean machine (for example Windows Sandbox) for each release; CI only builds the MSI.
- The update check is still to do (low priority).
