# 21: Use built-in AI skills

**What to build:** The "/" list in the AI panel shows Lignum's chat commands, skills shipped with Lignum, and the agent's own skills, next to the user's global and local skills.

**Blocked by:** None

**Status:** done (owner chose both: Lignum built-ins and the agent's own skills, plus chat commands)

- [x] Chat commands run in Lignum, never sent to the agent: `/clear`, `/new`, `/model [name]`, `/effort [level]`, `/permissions`, `/help`. Their names win over any skill.
- [x] Built-in skills embedded in the exe: `triage-errors`, `timeline`, `explain-exception`, `compare-runs`, `trace-to-source`. A global or local skill with the same name replaces one; `get_skill` and `list_skills` serve them too.
- [x] Claude Code's own skills (`~/.claude/skills`, including the ones synced from claude.ai) are listed as "Claude Code" and sent as typed (`/name text`). Checked by hand: in print mode Claude Code expands `/skill-creator` itself even with `--tools ""`, so its file and shell tools stay off (ADR 0002).
- [x] An agent skill whose name is taken by a command or a Lignum skill is hidden.
- [ ] Copilot, Cursor and Codex: no own skills listed yet. Copilot has skills (`~/.copilot/skills`, `.github/skills`), but none exist on the dev machine to check how print mode runs them. Override `CliAgent.OwnSkillFolders` once checked.
- [x] Tests: commands, built-ins and overriding, Claude skills listed and sent as typed (fake claude.cmd, fake skills folder)

**Note:** many of the synced claude.ai skills (pdf, docx, browser) expect tools Lignum keeps off; they will ask for Lignum's file and command tools or say they cannot.
