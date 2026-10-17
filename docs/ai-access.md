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

`list_tabs`, `search`, `read_lines` (at most 200 lines per call), `add_filter_rule`, `add_highlight_rule`, `add_bookmark` (with an optional note), `go_to_line`. They work on the selected tab. None of them change a log file.

## How it works

`Lignum.exe --mcp` opens no window. It is a stdio MCP server that forwards each tool call to the running Lignum over a named pipe that only your Windows user can open. The running Lignum only listens while AI access is on.
