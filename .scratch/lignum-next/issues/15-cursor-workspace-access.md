# 15: Cursor CLI does not get access to the workspace

**What's wrong:** Cursor does not get permission to access the workspace, even after "Yes" (trust) is chosen during sign-in.

**Blocked by:** None

**Status:** ready-for-human (code done; needs a manual check with a signed-in Cursor account)

**Cause:** Two things. (1) "Sign in" started a bare `agent` in the user profile folder, so the "Yes" trusted that folder rather than signing in for good (`agent status` then said "Not logged in"). (2) Turns run `agent -p` in the conversation folder, which was never trusted, and print mode cannot answer the trust prompt.

- [x] Reproduced: on this machine headless `agent -p` fails with "Authentication required… run 'agent login'"; `agent status` exits 0 even when not logged in
- [x] Sign in runs `agent login`; sign-in check reads `agent status` output
- [x] Turns pass `--trust --workspace <conversation folder>`
- [x] `--add-dir` for granted folders: not needed, since file access goes through Lignum's own tools (ADR 0002)
- [x] Argument test in `Lignum.App.Tests`
- [ ] Manual: sign in from Lignum, ask a question that uses a lignum tool, confirm it answers
