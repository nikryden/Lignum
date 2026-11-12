# 32: AI chat history sessions

**What to build:** Every AI chat is saved, and an older chat can be reopened and continued with full context.

**Blocked by:** none

**Status:** ready-for-agent

## Behaviour

- Each chat is saved to `%AppData%\Lignum\chats\<chat-id>.json` after every message: title (the first question, trimmed), created/updated time, agent, model and effort, entries, and the agent's own session id.
- The AI pane header gets **New chat**, and a **History** dropdown listing chats newest first (title + relative time). Each row has **Delete**. There is also **Delete all**.
- Picking a chat loads its transcript into the pane. The next question continues that conversation.
- **How context is carried on.** This follows Claude Code's `--resume`: the agent CLI keeps the transcript and sends it to the model again.
  - Claude: `--resume <session-id>`. The id is already generated in `Agents.cs`. It must come from the saved chat instead of a new GUID.
  - Codex: `codex exec resume <session-id>`. Copilot and Gemini use their resume flag if the installed version has one.
  - Fallback (custom command agent, missing session, or resume fails): send the previous turns as a "Conversation so far" block before the new prompt, capped at about the last 20 turns / 32 KB.
- If the chat's agent is no longer available, the transcript still shows and the fallback is used with the current agent.
- No file contents are stored beyond what was already in the chat text.

## Design

- `ChatHistoryStore` (load/list/save/delete, atomic write via temp file + rename). `ChatSession` model. `AssistantViewModel` gets `CurrentChat`, `NewChatCommand`, `OpenChatCommand`, `DeleteChatCommand`.
- `AgentBase` gets `ResumeSessionId` (in/out) instead of the private `_sessionId`.
- Tests: store round-trip, list ordering, argument building with and without a resume id, fallback prompt truncation.
