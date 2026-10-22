# 20: Token usage summary

**What to build:** Show tokens used per turn and per conversation, and the remaining budget when the agent reports it.

**Blocked by:** None

**Status:** done for Claude Code; Codex left open (see below)

- [x] Claude Code: reads `usage`, `total_cost_usd` and `modelUsage.*.contextWindow` from the `result` event, and the context in use from the last `assistant` message's usage
- [ ] Codex: `--json` token-count events. Not done: Codex is not installed on the dev machine, so its JSON output could not be checked, and switching its output to JSON changes how answers are read. Do it with a real `codex exec --json` sample.
- [x] Other agents: "Token usage: not reported by <agent>"
- [x] Assistant pane footer: last turn and conversation tokens (in = prompt including cached), cost when reported, context used / window / left
- [x] Parser tests with recorded output samples (fake claude.cmd)

**Note:** "remaining" is the context window left, not the subscription's remaining quota; none of the CLIs report that in print mode.
