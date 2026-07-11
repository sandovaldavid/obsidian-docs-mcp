---
title: Setup
nav_order: 2
---

# Setup
{: .no_toc }

1. TOC
{:toc}

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) — only needed to build from source; the published tool is self-contained and doesn't require the runtime.
- [Ollama](https://ollama.com/) installed and running at `http://localhost:11434`.
- The `nomic-embed-text` embedding model pulled in Ollama:
  ```bash
  ollama pull nomic-embed-text
  ```

## Install the MCP server

```bash
dotnet tool install -g obsidian-docs-mcp
```

This installs the `obsidian-docs-mcp` command as a .NET Global Tool, self-contained for your platform (Windows, macOS, or Linux — x64 and arm64).

## Configure your MCP client

### Claude Code (CLI)

Global (all projects):
```bash
claude mcp add --scope user --transport stdio obsidian-docs-mcp -- obsidian-docs-mcp
```

Local (this project only):
```bash
claude mcp add --scope project --transport stdio obsidian-docs-mcp -- obsidian-docs-mcp
```

### Claude Desktop

Add to your `claude_desktop_config.json`:

- Linux/WSL: `~/.config/Claude/claude_desktop_config.json`
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "obsidian-docs-mcp": {
      "command": "obsidian-docs-mcp"
    }
  }
}
```

You can also run `obsidian-docs-mcp setup` from a terminal to have the tool write this configuration for you automatically.

### Cursor

Add to `~/.cursor/mcp.json` (global) or `.cursor/mcp.json` in your project (local):

```json
{
  "mcpServers": {
    "obsidian-docs-mcp": {
      "command": "obsidian-docs-mcp"
    }
  }
}
```

### VS Code (GitHub Copilot Chat / MCP)

Create `.vscode/mcp.json` in your workspace. VS Code uses `servers` (not `mcpServers`) and requires an explicit `type`:

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

### Other MCP clients

Any client with MCP registry support can discover this server via its published manifest name `io.github.sandovaldavid/obsidian-docs-mcp` (see [`.mcp/server.json`](https://github.com/sandovaldavid/obsidian-docs-mcp/blob/main/ObsidianDocsMcp/.mcp/server.json)). For clients without registry support, the same pattern applies: a `stdio` server running the `obsidian-docs-mcp` command with no arguments — check your client's docs for its config file location and whether it expects `mcpServers` or `servers` as the top-level key.

## Initial indexing

The first time you use the server, the documentation index is empty. Build it with:

```bash
obsidian-docs-mcp index
```

This downloads the latest Obsidian Developer Docs and User Help content from GitHub, generates embeddings via Ollama, and stores everything in a local SQLite database. It can take a few minutes depending on your machine and Ollama's speed.

You can also trigger this from within an agent conversation by asking it to run the `ReindexDocumentation` tool — it runs in the background so it doesn't block your session.

## Building from source

```bash
git clone https://github.com/sandovaldavid/obsidian-docs-mcp.git
cd obsidian-docs-mcp
dotnet build -c Release ObsidianDocsMcp/ObsidianDocsMcp.csproj
dotnet pack -c Release ObsidianDocsMcp/ObsidianDocsMcp.csproj -o ./artifacts
dotnet tool install -g --add-source ./artifacts obsidian-docs-mcp
obsidian-docs-mcp index
```

To run directly from source without installing (no packaging step needed), copy `mcp-config.example.json` to `mcp-config.json`, replace `<ABSOLUTE_PATH_TO_PROJECT>` with your local path, and point your MCP client at that file.
