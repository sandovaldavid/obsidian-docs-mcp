---
title: Tools Reference
nav_order: 4
---

# Tools Reference
{: .no_toc }

1. TOC
{:toc}

The server exposes 3 MCP tools. All of them are safe to call repeatedly and require no arguments beyond what's listed below.

## `SearchDocumentation`

Hybrid (semantic + keyword) search over the indexed Obsidian documentation. Returns the most relevant logical fragments, ranked by combined relevance.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `query` | string | *(required)* | The user's question or a technical search term (e.g. `"how to create a plugin"` or `"WorkspaceLeaf"`). |
| `limit` | int | `3` | Maximum number of relevant fragments to return. |

**Usage tips:**
- For an exact API symbol or method (e.g. `registerView`, `WorkspaceLeaf`), include it verbatim in the query — FTS5 keyword matching will prioritize exact signature matches.
- For conceptual questions, natural language works well and can be asked in any language — the embedding model resolves the conceptual match across languages.
- Keep `limit` low (default `3`) to avoid flooding the context window; increase only if the first pass doesn't surface what you need.

**Returns:** a JSON array of result objects with `Title`, `Header`, `Content`, `FilePath`, `Score`, and `SourceType` (`"Hybrid"`).

## `ReindexDocumentation`

Triggers an asynchronous reindex: downloads the latest documentation from the official GitHub repositories, regenerates embeddings via Ollama, and rebuilds the SQLite index. Runs in the background so it doesn't block the current session — expect it to take a few minutes depending on your machine and Ollama's throughput. If a reindex is already in progress, calling this again returns immediately with a message instead of starting a second overlapping run.

No parameters. Call this when you suspect the local documentation is stale or missing methods from a recent Obsidian release.

## `IndexStatus`

Returns the current number of indexed documentation chunks, whether a reindex is currently in progress, and the error message from the last reindex if it failed. Useful to confirm the index has been built before relying on `SearchDocumentation`, or to sanity-check after a reindex.

No parameters.

## Example flow

1. Agent starts a fresh session, isn't sure if the index exists → calls `IndexStatus`.
2. If the count is `0`, the agent calls `ReindexDocumentation` and waits.
3. The agent answers the user's Obsidian plugin-development question by calling `SearchDocumentation(query: "registerEvent", limit: 3)`.
