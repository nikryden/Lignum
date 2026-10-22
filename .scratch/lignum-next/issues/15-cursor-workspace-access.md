# 15: Cursor CLI does not get access to the workspace

**What's wrong:** Cursor does not get permission to access the workspace, even after "Yes" (trust) is chosen during sign-in.

**Blocked by:** None

**Status:** ready-for-agent

**Likely cause:** `CursorCliAgent.BuildArguments` runs `agent -p` without `--trust`. In print mode the workspace-trust prompt cannot be answered, and the working folder is the conversation folder, not the one the user trusted at sign-in.

- [ ] Reproduce (headless run, check the output for the trust message)
- [ ] Pass `--trust` so the conversation folder is trusted
- [ ] Once ticket 17 exists, folders the user granted are added as workspace
- [ ] Argument test in `Lignum.App.Tests`
