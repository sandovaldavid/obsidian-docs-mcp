# Obsidian Docs MCP (.NET 10 + Ollama)

[![CI](https://github.com/sandovaldavid/obsidian-docs-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/sandovaldavid/obsidian-docs-mcp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/obsidian-docs-mcp.svg)](https://www.nuget.org/packages/obsidian-docs-mcp)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![skills.sh](https://skills.sh/b/sandovaldavid/obsidian-docs-mcp)](https://skills.sh/sandovaldavid/obsidian-docs-mcp)

A **Model Context Protocol (MCP)** server, packaged as a **.NET Global Tool**, that lets AI agents search the official Obsidian documentation extremely efficiently — both semantically and by exact keyword:

1. **Obsidian Developer Docs**: the official manual for building plugins and themes (TypeScript API).
2. **Obsidian Help**: the end-user manual for general app configuration.

The goal is to give your AI agent the most concise, accurate answer possible, drastically reducing context-window token usage.

📖 **Full documentation site**: https://sandovaldavid.github.io/obsidian-docs-mcp/

---

## 🚀 Key Features

- **Smart Hybrid Search**: combines deep semantic search (RAG, cosine similarity) with exact keyword search via SQLite FTS5 tokenization.
- **RRF Fusion (Reciprocal Rank Fusion)**: merges and re-ranks results from both search engines so the most relevant results surface first.
- **Persistent Offline SQLite Cache**: documentation is downloaded and processed once. Day-to-day searches run locally in milliseconds, with zero network latency.
- **Direct Dynamic Download**: no need to clone the official repos to disk. The indexer dynamically downloads ZIPs in memory from GitHub and segments them by logical headings (`#`, `##`, `###`).
- **Local Ollama**: uses Ollama's `nomic-embed-text` model on your own machine to generate embeddings, guaranteeing full privacy and zero cost.
- **Fast First Run**: on a fresh install, the server automatically downloads a periodically-regenerated prebuilt index instead of embedding everything locally, so search works within seconds — falls back to a live reindex only if that's unavailable.
- **Configurable Folder Filtering**: restrict indexing to specific top-level folders per source (e.g. `en,es,Sandbox` for User Help instead of all 32 language folders) to cut indexing time and chunk count.

---

## 📦 Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com/) installed and running at `http://localhost:11434`.
- The `nomic-embed-text` model pulled in Ollama:
  ```bash
  ollama pull nomic-embed-text
  ```

---

## 🛠️ Installation for End Users (Distribution)

Once the package is published on NuGet.org, anyone can install and configure it natively in seconds:

### 1. Install the MCP Server (global dotnet tool)
```bash
dotnet tool install -g obsidian-docs-mcp
```

### 2. Wire up the Skill + MCP Server

**Option A — One-command plugin (Claude Code, Codex, Antigravity CLI)**
These three have a real plugin system that bundles the skill and the MCP server registration into a single install. Each uses a completely different manifest format under the hood (`.claude-plugin/`, `.agents/plugins/` + `.codex-plugin/`, and a flat `plugin.json`, respectively — see [`plugins/obsidian-docs-mcp/`](plugins/obsidian-docs-mcp)), but all three were verified end-to-end locally: `validate` → `marketplace add`/clone → `install` → 1 skill + 1 MCP server enabled → clean `uninstall`.

Claude Code:
```bash
claude plugin marketplace add sandovaldavid/obsidian-docs-mcp
claude plugin install obsidian-docs-mcp@obsidian-docs-mcp
```

Codex:
```bash
codex plugin marketplace add sandovaldavid/obsidian-docs-mcp
codex plugin add obsidian-docs-mcp@obsidian-docs-mcp
```

Antigravity CLI (`agy`) — its `plugin install` only takes a local directory, so clone first:
```bash
git clone https://github.com/sandovaldavid/obsidian-docs-mcp
agy plugin install obsidian-docs-mcp/plugins/obsidian-docs-mcp
```

**Option B — Skill + MCP separately (every other client)**
Install the skill from the open catalog:
```bash
npx skills add sandovaldavid/obsidian-docs-mcp
```
This detects your installed agent(s) interactively. To target one explicitly instead:
```bash
npx skills add sandovaldavid/obsidian-docs-mcp -a claude-code    # Claude Code
npx skills add sandovaldavid/obsidian-docs-mcp -a antigravity    # Antigravity
npx skills add sandovaldavid/obsidian-docs-mcp -a codex          # Codex
npx skills add sandovaldavid/obsidian-docs-mcp -a github-copilot # GitHub Copilot
npx skills add sandovaldavid/obsidian-docs-mcp -a opencode       # OpenCode
```
Codex, Antigravity, OpenCode, GitHub Copilot, Cursor, Gemini CLI, Cline, and Zed all read the same universal `.agents/skills/` project directory, so one install (or cloning this repo, which commits it) covers all of them at once. Claude Code is the exception — it reads `.claude/skills/`, so its install creates a symlink there pointing back at the same canonical copy.

Then register the MCP server for your client — see "⚙️ Configuring the MCP Server in Clients" below.

---

## ⚙️ Configuring the MCP Server in Clients

Since the server runs as a global system command (`obsidian-docs-mcp`), registration is clean and native:

### 1. Claude Code (CLI)
To add the MCP server natively to Claude Code, run:

- **Global (all projects)**:
  ```bash
  claude mcp add --scope user --transport stdio obsidian-docs-mcp -- obsidian-docs-mcp
  ```
- **Local (this project only)**:
  ```bash
  claude mcp add --scope project --transport stdio obsidian-docs-mcp -- obsidian-docs-mcp
  ```

### 2. Codex (CLI)
```bash
codex mcp add obsidian-docs-mcp -- obsidian-docs-mcp
```

### 3. Antigravity CLI (`agy`)
No `mcp add` subcommand yet — add the server directly to `~/.gemini/antigravity-cli/mcp_config.json` (global; there's no per-project config file):
```json
{
  "mcpServers": {
    "obsidian-docs-mcp": {
      "command": "obsidian-docs-mcp"
    }
  }
}
```

### 4. Claude Desktop
Add the server to your `claude_desktop_config.json`:
- **Linux/WSL**: `~/.config/Claude/claude_desktop_config.json`
- **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "obsidian-docs-mcp": {
      "command": "obsidian-docs-mcp"
    }
  }
}
```

### 5. Cursor
Add the server to `~/.cursor/mcp.json` (global) or `.cursor/mcp.json` in your project (local):
```json
{
  "mcpServers": {
    "obsidian-docs-mcp": {
      "command": "obsidian-docs-mcp"
    }
  }
}
```

### 6. VS Code (GitHub Copilot Chat / MCP)
Create `.vscode/mcp.json` in your workspace — note VS Code uses `servers` (not `mcpServers`) and requires an explicit `type`:
```json
{
  "servers": {
    "obsidian-docs-mcp": {
      "type": "stdio",
      "command": "obsidian-docs-mcp"
    }
  }
}
```

### 7. Other MCP clients
Any client that supports the [MCP registry](https://modelcontextprotocol.io/) can discover this server via its published manifest: `io.github.sandovaldavid/obsidian-docs-mcp` (see [`.mcp/server.json`](ObsidianDocsMcp/.mcp/server.json)). For clients without registry support, the pattern above (a `stdio` server running the `obsidian-docs-mcp` command with no arguments) applies generically — check your client's docs for its specific config file location and key name (`mcpServers` vs `servers`).

---

## 🧠 Sync and Maintenance

Since the documentation cache is local and persistent (`obsidian_docs.db`), when you want to update the documentation with the latest releases from the GitHub repositories, simply trigger a reindex.

On the very first run, if the index is empty, the server does this automatically: it downloads a periodically-regenerated prebuilt index (fast, no local embedding compute) and only falls back to a live reindex if that's unavailable. You don't have to do anything for a fresh install to work — the commands below are for forcing a fresh reindex or restricting it to specific folders.

### From the console:
```bash
obsidian-docs-mcp index                     # reindex everything (default)
obsidian-docs-mcp index en,es,Sandbox       # restrict User Help to these top-level folders
obsidian-docs-mcp index en,es,Sandbox en    # also restrict Developer Docs
obsidian-docs-mcp index-status              # check chunk count / reindex progress / last error
```

### From the agent (MCP tool):
Any MCP client can tell the agent to run the `ReindexDocumentation` tool, optionally passing `userHelpFolders`/`developerDocsFolders` the same way as the CLI arguments above. This runs the download and vectorization process asynchronously in the background so it doesn't interrupt your chat session.

---

## 🛠️ Local Development Setup

Run directly from source without installing: copy `mcp-config.example.json` to `mcp-config.json`, replace `<ABSOLUTE_PATH_TO_PROJECT>` with your local path, and point your MCP client at it.

To build, pack, and install a local build as a global tool for end-to-end testing:
```bash
dotnet build -c Release ObsidianDocsMcp/ObsidianDocsMcp.csproj
dotnet pack -c Release ObsidianDocsMcp/ObsidianDocsMcp.csproj -o ./artifacts
dotnet tool uninstall -g obsidian-docs-mcp 2>/dev/null || true
dotnet tool install -g --add-source ./artifacts obsidian-docs-mcp
obsidian-docs-mcp index
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for more.

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions, commit conventions, and how to open a pull request.

## License

[MIT](LICENSE) © David Sandoval
