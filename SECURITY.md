# Security Policy

## Scope

`obsidian-docs-mcp` is a local MCP server that communicates over stdio. It does not expose any network port or public endpoint; it downloads documentation ZIPs from GitHub over HTTPS, talks to a local Ollama instance, and reads/writes a local SQLite database file. The attack surface is intentionally small, but reports are still welcome for issues such as:

- Unsafe handling of downloaded ZIP archives (e.g. path traversal, zip-slip).
- SQL injection in the FTS5/SQLite query paths.
- Arbitrary file read/write outside the intended database/cache location.
- Dependency vulnerabilities in the packaged NuGet dependencies.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities. Instead, use one of:

- [GitHub Security Advisories](https://github.com/sandovaldavid/obsidian-docs-mcp/security/advisories/new) for this repository, or
- Email **hello@sandovaldavid.com** with a description of the issue and reproduction steps.

This is a solo-maintained open-source project, so there's no formal SLA, but reports will be acknowledged and addressed as soon as reasonably possible.
