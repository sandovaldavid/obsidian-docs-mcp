---
name: obsidian-docs-retriever
description: Enables ultra-precise search and retrieval over the official Obsidian user and developer documentation while minimizing token consumption.
tools:
  - SearchDocumentation
  - IndexStatus
  - ReindexDocumentation
---

# Skill: Obsidian Docs Retriever

This skill helps AI agents interact optimally with the Obsidian documentation MCP server, ensuring precise answers with very low context-window token consumption.

## 🎯 When to use this Skill

Use this skill when the user asks questions related to:
- Developing plugins or themes for Obsidian (TypeScript API).
- Using and configuring Obsidian (shortcuts, Markdown, backlinks, core plugins, etc.).
- Looking up classes, methods, events, or objects in the official API (e.g. `WorkspaceLeaf`, `App`, `Vault`, `Workspace`).

## 🛠️ Available Tools

- `SearchDocumentation(query, limit)`: Hybrid search over the local SQLite database. Returns detailed logical fragments ranked by combined semantic relevance (Ollama embeddings) and keyword relevance (SQLite FTS5) via Reciprocal Rank Fusion (RRF).
- `IndexStatus()`: Reports the total number of documentation fragments currently registered in the local database, whether a reindex is in progress, and the last reindex error if one occurred.
- `ReindexDocumentation()`: Asynchronously downloads the latest documentation from the official GitHub repositories, generates embeddings via local Ollama, and rebuilds the local index.

## 💡 Guidelines for Optimal Use (Token Savings)

1. **Avoid bulk reads**: Never try to download or read entire markdown documentation files through generic disk-reading tools if you can query the specific section with `SearchDocumentation` instead.
2. **Exact technical queries**: If you're looking for a specific Obsidian API method or class, include it verbatim in the query (e.g. `query: "registerView"` or `query: "WorkspaceLeaf"`). Keyword search (FTS5) will prioritize exact matches on code signatures.
3. **Cross-language queries**: The official documentation is in English, but you can phrase the query in any language (e.g. `"how to register a command"` or a non-English equivalent) for general questions — Ollama's local multilingual semantic embeddings will resolve the conceptual match.
4. **Maintenance**: If you suspect the local documentation is outdated or missing methods from the latest release, start a refresh using `ReindexDocumentation`.
5. **Don't abuse the limit**: Keep the `limit` parameter low (default `3`) so the response contains only the most relevant fragments and doesn't overflow your context window.
