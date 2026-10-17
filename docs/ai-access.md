# AI access (MCP)

Lignum lets the AI tools you already use (GitHub Copilot in VS Code, Claude Desktop, Claude Code, Cursor and others) work with your open logs. The AI runs in your tool, on your own subscription; Lignum needs no API key and costs nothing extra.

## Turn it on

In Lignum: **View > AI access** (Ctrl+Shift+A), then **Turn on AI access**. While it is off, no AI tool can use Lignum. While it is on, the panel lists every call, and the lines an AI tool reads are sent to that tool's AI service.

## Connect your AI tool (once)

The panel's **Connect an AI tool** section copies the right setting for you. By hand, the MCP server is `Lignum.exe --mcp` (use the full path to your Lignum.exe):

- **VS Code / GitHub Copilot** (`.vscode/mcp.json`, or MCP: Add Server):
  `{ "servers": { "lignum": { "type": "stdio", "command": "C:\...\Lignum.exe", "args": ["--mcp"] } } }`
- **Claude Desktop** (`claude_desktop_config.json`) and **Cursor** (`~/.cursor/mcp.json`):
  `{ "mcpServers": { "lignum": { "command": "C:\...\Lignum.exe", "args": ["--mcp"] } } }`
- **Claude Code**: `claude mcp add lignum -- "C:\...\Lignum.exe" --mcp`

Keep Lignum open with your logs; then ask your AI tool things like "Which errors happen most in the log open in Lignum? Add a filter for them."

## Tools

- Looking: `list_tabs`, `select_tab`, `search`, `search_all_tabs` (every open tab at once), `find_in_files` (files in a folder that are not open), `read_lines` (at most 200 lines per call), `list_bookmarks`, `list_filter_rules`.
- Changing what you see: `show_search` (runs a search in the tab's results pane, inside the filter or on the whole file), `add_filter_rule`, `remove_filter_rule`, `set_filter_rule_enabled`, `clear_filter_rules`, `add_highlight_rule`, `add_bookmark` (with an optional note), `go_to_line`.

Tools that work on one tab take an optional `tab` (its index from `list_tabs`, or its file name) and use the selected tab otherwise. None of them change a log file.

## How it works

`Lignum.exe --mcp` opens no window. It is a stdio MCP server that forwards each tool call to the running Lignum over a named pipe that only your Windows user can open. The running Lignum only listens while AI access is on.

## Chat inside Lignum

With AI access on, the panel also has a chat. Pick the agent installed on this PC and ask about the open logs; it answers in the panel and can add filters, highlights and bookmarks:

- **Claude Code** (`claude`): uses your Claude subscription (Pro, Max, Team or Enterprise). Lignum runs it with only Lignum's tools (no file, shell or web tools).
- **GitHub Copilot CLI** (`copilot`): uses your GitHub Copilot subscription. Lignum runs it in prompt mode with the Lignum MCP server added.

The panel walks you through setup: **Install** runs the vendor's official installer in a PowerShell window you can see (Claude Code: `irm https://claude.ai/install.ps1 | iex`; Copilot CLI: `winget install GitHub.Copilot`). **Sign in** opens the agent in a terminal for its one-time sign-in (for Copilot CLI, type `/login`). **Check again** finds a newly installed agent without restarting Lignum.

Lignum starts the agent in the background for each question (a conversation continues until **New conversation**) and needs no API key. Each agent has its own usage limits and terms.
