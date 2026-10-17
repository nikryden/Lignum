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
- **OpenAI Codex CLI**: `codex mcp add lignum -- "C:\...\Lignum.exe" --mcp`
- **Windsurf** and **Antigravity** use the same mcpServers JSON as Claude Desktop (Antigravity: `.agents/mcp_config.json` or `~/.gemini/config/mcp_config.json`).

Keep Lignum open with your logs; then ask your AI tool things like "Which errors happen most in the log open in Lignum? Add a filter for them."

## Tools

- Looking: `list_tabs`, `select_tab`, `search`, `search_all_tabs` (every open tab at once), `find_in_files` (files in a folder that are not open), `read_lines` (at most 200 lines per call), `get_selection` (what you selected, at most 200 lines), `list_bookmarks`, `list_filter_rules`, `list_highlight_rules`.
- Changing what you see: `show_search` (runs a search in the tab's results pane, inside the filter or on the whole file), `add_filter_rule`, `remove_filter_rule`, `set_filter_rule_enabled`, `clear_filter_rules`, `set_hide_non_matching`, `add_highlight_rule`, `remove_highlight_rule`, `clear_highlight_rules`, `add_bookmark` (with an optional note and color: Blue, Red, Yellow, Green or Purple), `edit_bookmark`, `remove_bookmark`, `go_to_line`, `select_text` (selects text on a line, columns or whole lines, and scrolls to it), `show_full_line`.
- Skills: `list_skills`, `get_skill`, `save_skill` (see Skills below).
- Files: `open_file`, `close_tab`, `export_package` (a new `.lm` package with bookmarks, notes and colors; never overwrites a file).

Tools that work on one tab take an optional `tab` (its index from `list_tabs`, or its file name) and use the selected tab otherwise. None of them change a log file.

## How it works

`Lignum.exe --mcp` opens no window. It is a stdio MCP server that forwards each tool call to the running Lignum over a named pipe that only your Windows user can open. The running Lignum only listens while AI access is on.

## Chat inside Lignum

With AI access on, the panel also has a chat. Pick the agent installed on this PC and ask about the open logs; it answers in the panel and can add filters, highlights and bookmarks:

- **Claude Code** (`claude`): uses your Claude subscription (Pro, Max, Team or Enterprise). Lignum runs it with only Lignum's tools (no file, shell or web tools).
- **GitHub Copilot CLI** (`copilot`): uses your GitHub Copilot subscription. Lignum runs it in prompt mode with the Lignum MCP server added.
- **Cursor CLI** (`agent`): uses your Cursor account. Lignum puts its MCP server in the conversation folder's `.cursor/mcp.json` and runs `agent -p --approve-mcps`.
- **OpenAI Codex CLI** (`codex`): uses your ChatGPT plan. Lignum runs `codex exec` in a read-only sandbox with Lignum added through `-c mcp_servers.lignum...`. Installing it needs Node.js.
- **Custom agent**: any other agent CLI, for example Antigravity CLI (`agy`, Google's replacement for Gemini CLI). Enter its command and an argument template; `{prompt}` is the question, `{mcpConfig}` an MCP settings file (mcpServers JSON) with Lignum in it, `{lignum}` the path of Lignum.exe.

The panel walks you through setup: **Install** runs the vendor's official installer in a PowerShell window you can see (Claude Code: `irm https://claude.ai/install.ps1 | iex`; Copilot CLI: `winget install GitHub.Copilot`; Cursor CLI: `irm 'https://cursor.com/install?win32=true' | iex`; Codex CLI: `npm install -g @openai/codex`). **Sign in** opens the agent in a terminal for its one-time sign-in (for Copilot CLI, type `/login`). **Check again** finds a newly installed agent without restarting Lignum.

Lignum starts the agent in the background for each question (a conversation continues until **New conversation**) and needs no API key. Each agent has its own usage limits and terms.

## Skills

A skill is a set of saved instructions for the AI, such as "summarise the errors and bookmark the first of each". Type `/` at the start of a chat message to see your skills (↑/↓ choose, Tab or Enter inserts, Esc closes), then send `/name` and any extra text.

- **Global** skills are in `%APPDATA%\Lignum\skills\<name>\SKILL.md` and work for every log.
- **Local** skills are in `.lignum\skills\<name>\SKILL.md` in the selected log's folder, so they can travel with the logs. A local skill hides a global one with the same name.

The format is the same as Claude Code skills: front matter with `name:` and `description:`, then the instructions. The **Skills** button in the chat opens the global or local skills folder. To make a skill, ask the AI ("make a skill that finds the first error"): it saves it with `save_skill`, locally next to the log unless you ask for a global one, and only replaces an existing skill when you agree.

Skills work with every agent: Lignum does not paste the skill into the prompt. It tells the agent which skill you chose, and the agent reads the instructions with Lignum's `get_skill` tool. AI tools connected over MCP (Claude Desktop, VS Code) can use `list_skills` and `get_skill` too.
