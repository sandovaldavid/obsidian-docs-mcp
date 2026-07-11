# Contributing to Obsidian Docs MCP

Thanks for your interest in contributing! This is a small, solo-maintained project, so the process is kept lightweight.

## Building and running locally

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download), [Ollama](https://ollama.com/) with the `nomic-embed-text` model pulled.

```bash
dotnet restore
dotnet build
```

To run the server directly from source, copy `mcp-config.example.json` to `mcp-config.json`, replace `<ABSOLUTE_PATH_TO_PROJECT>` with your local absolute path, and point your MCP client (Claude Code, Cursor, VS Code, etc.) at it.

To build, pack, and install a local build as a global tool for end-to-end testing:

```bash
dotnet build -c Release ObsidianDocsMcp/ObsidianDocsMcp.csproj
dotnet pack -c Release ObsidianDocsMcp/ObsidianDocsMcp.csproj -o ./artifacts
dotnet tool uninstall -g obsidian-docs-mcp 2>/dev/null || true
dotnet tool install -g --add-source ./artifacts obsidian-docs-mcp
obsidian-docs-mcp index
```

## Code formatting

This project uses `dotnet format` (built into the .NET SDK) with the rules defined in [`.editorconfig`](.editorconfig). Before committing:

```bash
dotnet format ObsidianDocsMcp/ObsidianDocsMcp.csproj
```

A Husky.Net pre-commit hook runs `dotnet format --verify-no-changes` automatically on staged `.cs` files and blocks the commit if anything is unformatted — the same check runs in CI ([`.github/workflows/format.yml`](.github/workflows/format.yml)). Hooks install automatically the first time you run `dotnet restore`; if they don't, run `dotnet tool restore && dotnet husky install` once after cloning.

## Commit conventions

This project uses [Conventional Commits](https://www.conventionalcommits.org/) for every commit merged to `main`, **with a mandatory scope**. This is required both for [release-please](https://github.com/googleapis/release-please) (which parses commit history to determine version bumps and generate `CHANGELOG.md`) and because a Husky.Net `commit-msg` hook rejects commits that don't match the pattern.

```
type(scope): imperative, lowercase description
```

- **Type**: one of `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`, `arch`, `config`, `lint`.
- **Scope**: the affected area, lowercase. Common scopes in this repo: `server` (`Program.cs`, host wiring), `tools` (MCP tool methods), `indexer` (`ObsidianIndexer`), `db` (`DatabaseService`), `models`, `docs` (the `docs/` site), `readme`, `github` (workflows/templates), `husky`, `skill` (`SKILL.md`), `deps`, `repo` (repo-wide hygiene).
- **Description**: imperative mood ("add", not "added"/"adds"), starts lowercase, no trailing period, keep the whole header to 50 characters or less.
- Use `type(scope)!: ...` plus a `BREAKING CHANGE:` footer for breaking changes.

Examples:
```
feat(tools): add reindex progress to IndexStatus
fix(db): dispose blob stream in VectorSearchAsync
docs(setup): update instructions for Windows
ci(github): add dotnet format workflow
```

## Branching model

- **`develop`** is the integration branch — target this with feature/fix branches.
- **`main`** only receives merges from `develop` (or a hotfix branch) and is what release-please and the docs/NuGet publish pipelines watch. Pushing to `main` or `develop` directly is blocked; both only accept changes via pull request, and neither branch can be deleted.
- PRs into **`develop`** merge via **squash** or **rebase** (no merge commits — keeps `develop`'s history linear).
- PRs into **`main`** merge via **squash** or **merge commit** (no rebase — preserves the merge point from `develop`).
- Every PR must pass the `build` (CI) and `dotnet-format` checks before it's mergeable.
- Feature branches are auto-deleted after merge; `main`/`develop` never are.

## Pull requests

1. Fork the repo and create a branch from `develop`.
2. Make your changes, keeping commits Conventional-Commits-formatted with a scope.
3. Confirm `dotnet build -c Release`, `dotnet pack -c Release`, and `dotnet format --verify-no-changes` succeed locally.
4. Update relevant docs (`README.md`, `docs/`) if the change is user-facing.
5. Open a pull request against `develop` describing the change and the motivation behind it.

## Reporting bugs / requesting features

Please use the GitHub issue templates (Bug Report / Feature Request) rather than opening a blank issue — they make sure the essential context (OS, .NET/Ollama versions, reproduction steps) is captured up front.
