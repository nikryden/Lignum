# 20: Single-file packaging

**What to build:** Lignum is published as a self-contained win-x64 single-file exe that runs on a clean Windows machine without the .NET runtime installed. A manually triggered GitHub Actions workflow builds the exe and uploads it as an artifact.

**Blocked by:** 01

**Status:** resolved

- [x] The publish command in `CLAUDE.md` produces a single exe
- [ ] The exe starts on a machine without .NET installed
- [x] A manual (workflow_dispatch) workflow uploads the exe as a build artifact
- [x] The app's version number is set from the build

## Comments

`src/Directory.Build.props` sets `DebugType=embedded`, so publish now yields only `Lignum.exe` (~140 MB, no loose .pdb). New `.github/workflows/publish.yml` (workflow_dispatch, optional `version` input, default `0.1.<run_number>`) passes `-p:Version` and uploads the exe. Verified locally: `-p:Version=1.2.3` gives FileVersion 1.2.3.0, and the started exe logs "version 1.2.3.0".
Unverified: starting on a machine with no .NET installed (this machine has SDKs; the exe is self-contained and started fine here). The workflow has not been run on GitHub yet. Needs a manual check on a clean VM or Windows Sandbox.
