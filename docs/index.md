---
title: Home
nav_order: 1
description: "Obsidian Docs MCP — hybrid search over Obsidian's documentation for AI agents."
permalink: /
---

# Obsidian Docs MCP
{: .fs-9 }

A [Model Context Protocol](https://modelcontextprotocol.io/) server that gives AI agents fast, precise, token-efficient search over the official Obsidian documentation.
{: .fs-6 .fw-300 }

[Get started](setup){: .btn .btn-primary .fs-5 .mb-4 .mb-md-0 .mr-2 }
[View on GitHub](https://github.com/sandovaldavid/obsidian-docs-mcp){: .btn .fs-5 .mb-4 .mb-md-0 }

---

## What is this?

`obsidian-docs-mcp` is a .NET Global Tool packaged as an MCP server. It lets AI coding agents (Claude Code, Claude Desktop, Cursor, VS Code, etc.) answer questions about:

1. **Obsidian Developer Docs** — the official manual for building plugins and themes (TypeScript API): classes like `App`, `Vault`, `Workspace`, `WorkspaceLeaf`, events, and lifecycle hooks.
2. **Obsidian Help** — the end-user manual: settings, Markdown syntax, backlinks, core plugins, keyboard shortcuts.

Instead of an agent reading entire documentation files into its context window (expensive and imprecise), it calls a search tool that returns only the most relevant snippets.

## Why hybrid search?

Documentation questions come in two flavors: **exact** ("what does `registerView` do?") and **conceptual** ("how do I make a note read-only?"). A single search strategy is bad at one of the two. This server combines:

- **Keyword search** (SQLite FTS5, Porter tokenizer) — wins on exact API names, method signatures, config keys.
- **Semantic search** (local embeddings via [Ollama](https://ollama.com/), `nomic-embed-text`) — wins on natural-language, conceptual, or cross-language queries.

Both result sets are merged with **Reciprocal Rank Fusion (RRF)**, so whichever strategy finds the better match wins, without you having to pick one. See [Architecture](architecture) for the full pipeline.

## Zero-setup documentation sync

You don't need to clone Obsidian's doc repositories. The indexer downloads them as ZIP archives directly from GitHub, in memory, and builds a local SQLite cache — so searches after the first index run entirely offline with no network latency.

## Quick links

| | |
|---|---|
| [Setup](setup) | Install the tool and configure it in your MCP client |
| [Architecture](architecture) | How indexing, hybrid search, and RRF fusion work |
| [Tools Reference](tools-reference) | The 3 MCP tools this server exposes |
| [FAQ](faq) | Troubleshooting common issues |
| [Contributing](contributing) | Branching model, commit conventions, and how to build locally |
| [Changelog](changelog) | Release history |
