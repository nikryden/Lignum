# 31: Move "Connect an AI tool" into Settings

**What to build:** The **Connect an AI tool** expander leaves the AI pane and moves to the Settings window's AI page.

**Blocked by:** 30

**Status:** ready-for-agent

## Behaviour

- Remove the `Connect an AI tool` expander from `AssistantPanel.xaml`.
- The Settings ▸ AI page shows the same text and copy-config buttons (VS Code / Copilot `mcp.json`, and `mcpServers` JSON for Claude Desktop, Cursor, Windsurf and Antigravity).
- New menu item **View ▸ Connect an AI tool…**, plus a small link/button in the AI pane (shown when AI access is off), which opens Settings on the AI page.
