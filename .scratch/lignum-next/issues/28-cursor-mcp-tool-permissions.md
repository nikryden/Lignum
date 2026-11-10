# 28: Cursor CLI rejects Lignum MCP tools in print mode

**What's wrong:** With Cursor selected as the in-app agent, skill prompts (and other lignum tool calls) fail with `User rejected MCP: lignum-get_skill`. GitHub Copilot works. The user never sees a usable Allow prompt because Lignum runs `agent -p` without a TTY.

**Blocked by:** None

**Status:** ready-for-human (code done; needs a manual check with Cursor selected in the AI panel)

**Cause:** `--approve-mcps` only auto-approves connecting the MCP *server*. Cursor still prompts for each *tool* call. Copilot already passes `--allow-tool lignum`; Cursor had no equivalent, so print mode declined the call.

- [x] Write `.cursor/cli.json` with `permissions.allow: ["Mcp(lignum:*)"]` and `permissions.deny: []` (Cursor's schema requires both arrays; same role as Copilot's `--allow-tool lignum`)
- [x] Do **not** pass `--force`/`--yolo`: that would also auto-allow Cursor's own shell/write tools; Lignum gates those via ADR 0002
- [x] Keep `--approve-mcps` for server connect + `--trust --workspace` from ticket 15
- [x] Argument/config test in `Lignum.App.Tests`
- [ ] Manual: with Cursor selected, run `/filter` (or another skill) and confirm `get_skill` runs without a rejection
