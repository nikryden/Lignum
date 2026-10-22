# 16: Select model and reasoning level

**What to build:** Let the user choose the model and reasoning level (effort) for the AI assistant, per agent.

**Blocked by:** None

**Status:** ready-for-agent

**Mapping:** Claude Code `--model` (+ effort where the CLI supports it); Codex `-m` and `-c model_reasoning_effort=low|medium|high`; Cursor `--model`; Copilot `--model`; Custom agent: `{model}` / `{effort}` placeholders in the argument template.

- [ ] Model (editable combo with known models) and reasoning-level dropdowns in the assistant pane
- [ ] Saved per agent in `AssistantSettings`
- [ ] Empty means the CLI's default (no flag passed)
- [ ] Controls an agent does not support are hidden
- [ ] Argument tests for each agent
