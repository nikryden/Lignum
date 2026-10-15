# 20: Single-file packaging

**What to build:** Lignum is published as a self-contained win-x64 single-file exe that runs on a clean Windows machine without the .NET runtime installed. A manually triggered GitHub Actions workflow builds the exe and uploads it as an artifact.

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] The publish command in `CLAUDE.md` produces a single exe
- [ ] The exe starts on a machine without .NET installed
- [ ] A manual (workflow_dispatch) workflow uploads the exe as a build artifact
- [ ] The app's version number is set from the build
