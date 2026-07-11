# Obsidian Docs MCP

A **Model Context Protocol (MCP)** server, distributed as a .NET Global Tool, that gives AI agents fast, token-efficient search over the official Obsidian documentation:

- **Obsidian Developer Docs** — the plugin/theme development manual (TypeScript API).
- **Obsidian Help** — the end-user manual (settings, Markdown syntax, core plugins, etc.).

It combines SQLite FTS5 keyword search with local semantic embeddings (via [Ollama](https://ollama.com/)) merged through Reciprocal Rank Fusion, so agents get the most relevant documentation snippets without burning context tokens.

## Prerequisites

- [.NET 10 runtime](https://dotnet.microsoft.com/download) is **not** required — this tool ships self-contained.
- [Ollama](https://ollama.com/) running locally with the `nomic-embed-text` model pulled:
  ```bash
  ollama pull nomic-embed-text
  ```

## Install

```bash
dotnet tool install -g obsidian-docs-mcp
```

## MCP Tools

| Tool | Description |
|---|---|
| `SearchDocumentation(query, limit)` | Hybrid (keyword + semantic) search over the indexed Obsidian docs. |
| `ReindexDocumentation()` | Downloads the latest docs from GitHub and rebuilds the local index. |
| `IndexStatus()` | Returns the number of indexed documentation chunks. |

## Quick configuration

```json
{
  "mcpServers": {
    "obsidian-docs-mcp": {
      "command": "obsidian-docs-mcp"
    }
  }
}
```

## Links

- Full documentation: https://sandovaldavid.github.io/obsidian-docs-mcp/
- Source code: https://github.com/sandovaldavid/obsidian-docs-mcp
- MCP specification: https://modelcontextprotocol.io/
