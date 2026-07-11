---
title: FAQ
nav_order: 5
---

# FAQ / Troubleshooting
{: .no_toc }

1. TOC
{:toc}

## "Vector search failed" / embeddings not working

The server falls back to keyword-only search when embeddings can't be generated. This usually means Ollama isn't reachable. Check:

```bash
curl http://localhost:11434/api/tags
```

If this fails, start Ollama and make sure it's listening on `http://localhost:11434` (the default). If it's running but the model is missing:

```bash
ollama pull nomic-embed-text
```

## The index is empty / `IndexStatus` returns 0

You need to run the initial index at least once:

```bash
obsidian-docs-mcp index
```

or ask your agent to call the `ReindexDocumentation` tool.

## Reindexing is slow

Reindexing downloads two full documentation repositories and generates an embedding per section via a local Ollama call — this is CPU/GPU-bound on your machine, not network-bound after the initial download. A few minutes is normal; it only needs to run again when you want fresher docs, not on every search.

## How do I use a local checkout of the docs instead of downloading from GitHub?

Set `Docs:DeveloperDocsPath` and/or `Docs:UserHelpPath` in `appsettings.json` (or via environment variable override) to point at local clones of [`obsidian-developer-docs`](https://github.com/obsidianmd/obsidian-developer-docs) and [`obsidian-help`](https://github.com/obsidianmd/obsidian-help). If the path exists on disk, the indexer reads it directly instead of downloading a ZIP.

## Where is the database stored?

By default, wherever `Database:Path` in `appsettings.json` points (`obsidian_docs.db` relative to the working directory when running from source). When installed as a global tool without an explicit path, it defaults to a per-user local app data folder.

## My question isn't answered here

Please [open an issue](https://github.com/sandovaldavid/obsidian-docs-mcp/issues/new/choose) with as much detail as possible (OS, .NET version, Ollama version, steps to reproduce).
