# 20: Token usage summary

**What to build:** Show tokens used per turn and per conversation, and the remaining budget when the agent reports it.

**Blocked by:** None

**Status:** ready-for-agent

- [ ] Claude Code: `--output-format stream-json`, read `usage` from the result event
- [ ] Codex: `--json`, read token-count events
- [ ] Other agents: show "n/a" when no usage is reported
- [ ] Assistant pane footer: input / output / total tokens, context window remaining when known
- [ ] Parser tests with recorded output samples
