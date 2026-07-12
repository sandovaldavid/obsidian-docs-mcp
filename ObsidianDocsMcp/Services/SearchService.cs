using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ObsidianDocsMcp.Models;

namespace ObsidianDocsMcp.Services;

/// <summary>
/// Core hybrid-search pipeline (FTS5 keyword + vector search fused with RRF), independent of
/// the MCP transport so it can be exercised directly by tests and the evaluation harness.
/// </summary>
public class SearchService
{
    private readonly IDatabaseService _dbService;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        IDatabaseService dbService,
        IEmbeddingService embeddingService,
        ILogger<SearchService> logger)
    {
        _dbService = dbService;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<List<SearchResult>> SearchAsync(string query, int limit)
    {
        // 1. Run FTS5 (keyword) search
        var ftsResults = await _dbService.FtsSearchAsync(query, limit * 2);

        // 2. Get the query embedding and run the vector (semantic) search
        List<SearchResult> vectorResults = new();
        try
        {
            var queryVector = await _embeddingService.GetEmbeddingAsync(EmbeddingTextFormatter.FormatQuery(query));
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
        return RankFusion.ReciprocalRankFusion(vectorResults, ftsResults, limit);
    }
}
