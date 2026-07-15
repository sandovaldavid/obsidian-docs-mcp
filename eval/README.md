# Retrieval quality evaluation

This folder holds the data for the `ObsidianDocsMcp.Eval` harness, which measures how well the
hybrid search retrieves the right documentation for a fixed set of annotated queries. Any change
to the embedding model, prompt prefixes, chunking, or search parameters should be justified by
these numbers — not by spot-checking a few queries by hand.

## Files

- `golden-set.json` — 40 real queries annotated with the doc files a good retriever should
  return. Entries are keyed by **file path** (plus an optional header prefix), never by chunk
  IDs, whose positional component shifts whenever the upstream docs change. `docsSnapshot` pins
  the exact doc-repo commits the eval index is built from, so metric changes reflect code
  changes rather than upstream doc churn.
- `baseline.json` — the committed reference report the `compare` command guards against.
  Regenerate it (see below) whenever a change is adopted.
- `results/` — per-run reports (gitignored).

## Metrics

Recall@k and Precision@k for k ∈ {1, 3, 5, 10}, MRR, and nDCG@10. **Recall@3 and MRR are the
headline metrics** — the MCP tool returns the top 3 results by default, so what matters most is
whether the right document appears in those three, and how high.

## Running (requires a local Ollama with the embedding model pulled)

```bash
# 1. Build the eval index from the pinned doc snapshots (~10-20 min: embeds every chunk)
dotnet run --project ObsidianDocsMcp.Eval -- build-index \
  --golden-set eval/golden-set.json --db /tmp/eval.db

# 2. Check that every annotated path still exists in the corpus
dotnet run --project ObsidianDocsMcp.Eval -- validate \
  --golden-set eval/golden-set.json --db /tmp/eval.db

# 3. Score the golden set and write a report
dotnet run --project ObsidianDocsMcp.Eval -- run \
  --golden-set eval/golden-set.json --db /tmp/eval.db --label my-experiment

# 4. Compare against the committed baseline (nonzero exit on headline regressions > epsilon)
dotnet run --project ObsidianDocsMcp.Eval -- compare \
  --baseline eval/baseline.json --candidate eval/results/my-experiment.json --fail-on-regression
```

To establish or refresh the baseline, run steps 1-3 with `--label baseline` on the current
configuration and copy `eval/results/baseline.json` to `eval/baseline.json`.

To compare embedding models, build one index per model (`--model qwen3-embedding:0.6b`, etc.),
`run` each with its own label, and `compare` the reports pairwise. The index build restricts
User Help to the `en` folder by default (the golden set is English-only); pass
`--user-help-folders en,es,Sandbox` to mirror the production index instead.

## Maintaining the golden set

- Run `validate` after changing annotations or bumping `docsSnapshot` — it lists annotated
  paths missing from the index and suggests likely replacements by filename.
- When bumping the snapshot, update both `*Ref` and the matching `*ZipUrl` commit hashes.
- Keep queries mixed: exact API symbols, how-to phrasings, user-help topics, and a few typo
  variants. Every query needs at least one `grade >= 1` annotation.

The GitHub Actions workflow `.github/workflows/eval.yml` runs all of this weekly and on demand
(it installs Ollama in the runner), uploads the report as an artifact, and fails on headline
regressions once `eval/baseline.json` exists.
