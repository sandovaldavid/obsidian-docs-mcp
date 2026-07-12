using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ObsidianDocsMcp.Models;
using ObsidianDocsMcp.Services;

namespace ObsidianDocsMcp.Tools;

[McpServerToolType]
public class ObsidianSearchTools
{
    private readonly IDatabaseService _dbService;
    private readonly IEmbeddingService _embeddingService;
    private readonly ObsidianIndexer _indexer;
    private readonly ILogger<ObsidianSearchTools> _logger;

    // The MCP C# SDK resolves this constructor using the host's DI container.
    public ObsidianSearchTools(
        IDatabaseService dbService,
        IEmbeddingService embeddingService,
        ObsidianIndexer indexer,
        ILogger<ObsidianSearchTools> logger)
    {
        _dbService = dbService;
        _embeddingService = embeddingService;
        _indexer = indexer;
        _logger = logger;
    }

    [McpServerTool, Description("Performs a hybrid (semantic and keyword) search over the Obsidian documentation (user and developer manuals), returning the most relevant sections.")]
    public async Task<string> SearchDocumentation(
        [Description("The user's question or a technical search term (e.g. 'how to create a plugin' or 'WorkspaceLeaf').")] string query,
        [Description("Maximum number of relevant fragments to return (default 3).")] int limit = 3)
    {
        _logger.LogInformation("MCP Tool called: SearchDocumentation with query '{Query}' and limit {Limit}", query, limit);

        try
        {
            // 1. Run FTS5 (keyword) search
            var ftsResults = await _dbService.FtsSearchAsync(query, limit * 2);

            // 2. Get the query embedding and run the vector (semantic) search
            List<SearchResult> vectorResults = new();
            try
            {
                var queryVector = await _embeddingService.GetEmbeddingAsync(query);
                if (queryVector.Length > 0)
                {
                    vectorResults = await _dbService.VectorSearchAsync(queryVector, limit * 2);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Vector search failed due to embedding generation error: {Msg}. Falling back to FTS keyword search only.", ex.Message);
            }

            // 3. Merge results using Reciprocal Rank Fusion (RRF)
            var mergedResults = ReciprocalRankFusion(vectorResults, ftsResults, limit);

            if (mergedResults.Count == 0)
            {
                return "No relevant results were found in the Obsidian documentation.";
            }

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(mergedResults, jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing SearchDocumentation tool.");
            return $"Error processing the search: {ex.Message}";
        }
    }

    [McpServerTool, Description("Starts a reindex process that reads all Markdown files, generates their embeddings via Ollama, and stores them in SQLite.")]
    public Task<string> ReindexDocumentation(
        [Description("Comma-separated top-level User Help folders to index, e.g. 'en,es,Sandbox'. Omit to index every language (default).")] string? userHelpFolders = null,
        [Description("Comma-separated top-level Developer Docs folders to index. Omit to index everything (default).")] string? developerDocsFolders = null)
    {
        _logger.LogInformation("MCP Tool called: ReindexDocumentation");

        // The gate is acquired synchronously here so we can report immediately if a reindex is
        // already running, instead of allowing concurrent reindexes to race each other.
        if (!_indexer.TryBeginReindex())
        {
            return Task.FromResult("A reindex is already in progress. Wait for it to finish before starting another one (use IndexStatus to check progress).");
        }

        // Run indexing asynchronously so we don't block the MCP transport.
        _ = Task.Run(async () =>
        {
            try
            {
                await _indexer.IndexAllDocsAsync(userHelpFolders, developerDocsFolders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background reindexation failed.");
            }
            finally
            {
                _indexer.EndReindex();
            }
        });

        return Task.FromResult("Reindexing has started in the background. This can take a few minutes depending on the number of files and Ollama's speed. Use IndexStatus to check progress or a possible error.");
    }

    [McpServerTool, Description("Returns the current status of the Obsidian documentation index, including whether a reindex is in progress and the last error if the previous reindex failed.")]
    public async Task<string> IndexStatus()
    {
        _logger.LogInformation("MCP Tool called: IndexStatus");
        try
        {
            var count = await _dbService.GetTotalChunksCountAsync();
            var status = $"The index currently has {count} documentation sections registered.";

            if (_indexer.IsReindexing)
            {
                status += " A reindex is currently in progress.";
            }
            else if (_indexer.LastReindexError != null)
            {
                status += $" The last reindex failed: {_indexer.LastReindexError}";
            }

            return status;
        }
        catch (Exception ex)
        {
            return $"Error getting index status: {ex.Message}";
        }
    }

    /// <summary>
    /// Merges two result lists using Reciprocal Rank Fusion (RRF). RRF only decides the merged
    /// *order* — it's not returned to callers, since its magnitude (~1/k) isn't a meaningful
    /// relevance measure on its own. The returned <see cref="SearchResult.MatchPercent"/> is the
    /// best of the underlying cosine-similarity/BM25-derived percentages instead, so callers get
    /// an interpretable 0-100 match quality regardless of which method(s) found the result.
    /// </summary>
    private List<SearchResult> ReciprocalRankFusion(List<SearchResult> vectorList, List<SearchResult> ftsList, int limit)
    {
        const double k = 60.0; // Standard RRF smoothing constant
        var rrfScores = new Dictionary<string, (SearchResult Doc, double RrfScore, double BestMatchPercent)>();

        // Computes and accumulates RRF scores for a result list, tracking the best original
        // match percentage seen for each fragment across both lists.
        void ApplyRrf(List<SearchResult> docList)
        {
            for (int rank = 0; rank < docList.Count; rank++)
            {
                var doc = docList[rank];
                // Uniquely identify a fragment by its file path and heading.
                var key = $"{doc.FilePath}_{doc.Header}";
                double rrfContribution = 1.0 / (k + (rank + 1));

                if (rrfScores.TryGetValue(key, out var entry))
                {
                    rrfScores[key] = (entry.Doc, entry.RrfScore + rrfContribution, Math.Max(entry.BestMatchPercent, doc.MatchPercent));
                }
                else
                {
                    rrfScores[key] = (doc, rrfContribution, doc.MatchPercent);
                }
            }
        }

        ApplyRrf(vectorList);
        ApplyRrf(ftsList);

        // Sort by descending RRF score (internal ranking only) and take the limit.
        var finalResults = rrfScores.Values
            .OrderByDescending(x => x.RrfScore)
            .Select(x =>
            {
                var doc = x.Doc;
                doc.MatchPercent = Math.Round(x.BestMatchPercent, 1);
                doc.SourceType = "Hybrid";
                return doc;
            })
            .Take(limit)
            .ToList();

        return finalResults;
    }
}
