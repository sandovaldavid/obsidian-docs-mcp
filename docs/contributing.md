---
title: Contributing
nav_order: 6
---

# Contributing
{: .no_toc }

1. TOC
{:toc}

This is a small, solo-maintained project, so the contribution process is kept lightweight. The full guide lives in [`CONTRIBUTING.md`](https://github.com/sandovaldavid/obsidian-docs-mcp/blob/main/CONTRIBUTING.md) in the repo — this page is a quick summary.

## Branching model

- **`develop`** is the integration branch — target this with feature/fix branches.
- **`main`** only receives merges from `develop` and is what the release and docs/NuGet publish pipelines watch. Direct pushes to either branch are blocked; both only accept changes via pull request.
- PRs into both `develop` and `main` merge via squash or merge commit (no rebase). Use a merge commit specifically when syncing `main` back into `develop` after a release, to keep commit ancestry intact.
- Every PR must pass the `build` and `dotnet-format` checks before it's mergeable.

## Building and running locally

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download), [Ollama](https://ollama.com/) with the `nomic-embed-text` model pulled.

```bash
dotnet restore
dotnet build
```

To run the server directly from source, copy `mcp-config.example.json` to `mcp-config.json`, replace `<ABSOLUTE_PATH_TO_PROJECT>` with your local absolute path, and point your MCP client at it.

## Code formatting

This project uses `dotnet format` with the rules in [`.editorconfig`](https://github.com/sandovaldavid/obsidian-docs-mcp/blob/main/.editorconfig). A [Husky.Net](https://alirezanet.github.io/Husky.Net/) pre-commit hook runs `dotnet format --verify-no-changes` automatically on staged `.cs` files, and the same check runs in CI. Hooks install automatically the first time you run `dotnet restore`.

## Commit conventions

Every commit uses [Conventional Commits](https://www.conventionalcommits.org/) **with a mandatory scope**:

```
type(scope): imperative, lowercase description
```

A Husky.Net `commit-msg` hook rejects commits that don't match the pattern. See [`CONTRIBUTING.md`](https://github.com/sandovaldavid/obsidian-docs-mcp/blob/main/CONTRIBUTING.md#commit-conventions) for the full list of types and scopes used in this repo.

## Reporting bugs / requesting features

Please use the [GitHub issue templates](https://github.com/sandovaldavid/obsidian-docs-mcp/issues/new/choose) (Bug Report / Feature Request) rather than opening a blank issue.
