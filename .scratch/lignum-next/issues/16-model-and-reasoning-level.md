# 16: Select model and reasoning level

**What to build:** Let the user choose the model and reasoning level (effort) for the AI assistant, per agent.

**Blocked by:** None

**Status:** done

**Mapping (checked against each CLI's --help):** Claude Code `--model` + `--effort low|medium|high|xhigh|max`; Copilot `--model` + `--reasoning-effort none..max`; Codex `-m` + `-c model_reasoning_effort=`; Cursor `--model` only (effort is part of some model names); Custom agent: `{model}` / `{effort}` placeholders, dropped with their flag when empty.

- [x] Model (editable combo with known models) and reasoning-level dropdowns in the assistant pane
- [x] Saved per agent in `AssistantSettings`
- [x] Empty means the CLI's default (no flag passed)
- [x] Controls an agent does not support are hidden
- [x] Argument tests for each agent
