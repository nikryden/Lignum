# 05: AI help with pattern matching (e.g. Claude, via MCP)

**What to build:** Let an AI assistant such as Claude help with logs. The first goal is pattern matching: for example "write a regex for all request IDs", "explain this stack trace", or "find lines related to this error".

**Blocked by:** None (needs design first)

**Status:** in progress (MCP server and in-app chat done; Copy for AI and a live check of Copilot CLI's flags remain)

## Options to decide between
- **A. Lignum as an MCP server:** Lignum exposes tools (search, read a line range, add a filter or highlight rule) that Claude Desktop or Claude Code can call. No API key in Lignum, and the user's own Claude does the thinking.
- **B. Built-in assistant:** Lignum calls the Claude API directly from a side panel. This needs API-key settings, and log lines get sent to the API.

## Open questions
- Which option, A or B (or A first)?
- Privacy: logs can contain sensitive data. What is sent, and does the user confirm it first?
- Should the AI only suggest regexes and rules, or also apply them?

## Decisions (2026-10-17)
- Option B: a built-in assistant that calls the Claude API (official `Anthropic` NuGet SDK, model `claude-opus-5-5` by default, streamed).
- Privacy: off until the user turns it on. The user's own API key is stored encrypted with DPAPI (or `ANTHROPIC_API_KEY` is used). Each read is shown in the conversation ("Claude read N lines from X") and every tool call is logged.
- Claude may suggest and apply: filter rules, highlight rules, bookmarks with notes, go to line. It never edits a log file. Reads are capped at 200 lines per call.

## Change of plan (2026-10-17)
- The Claude API version was removed: it needs paid API credits on top of the user's subscriptions.
- Option A instead: `Lignum.exe --mcp` is an MCP server for any AI tool (GitHub Copilot, Claude Desktop/Code, Cursor...), relaying to the running Lignum over a per-user named pipe. AI access is off until turned on; the side panel lists every call. Setup: `docs/ai-access.md`.
- Next: an in-app chat that runs the user's installed agent CLI (Claude Code, GitHub Copilot CLI) on their own subscription, with Lignum's MCP tools attached; then "Copy for AI".
