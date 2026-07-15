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
    // Keep the RRF candidate pool stable for the MCP default (3) and eval depth (10), so the
    // evaluated top three are the same top three returned to MCP clients.
    private const int MinimumFusionCandidates = 20;

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
        var candidateLimit = Math.Max(limit * 2, MinimumFusionCandidates);

        // 1. Run FTS5 (keyword) search
        var ftsResults = await _dbService.FtsSearchAsync(query, candidateLimit);

        // 2. Get the query embedding and run the vector (semantic) search
        List<SearchResult> vectorResults = new();
        try
        {
            var queryVector = await _embeddingService.GetEmbeddingAsync(EmbeddingTextFormatter.FormatQuery(query));
            if (queryVector.Length > 0)
            {
                vectorResults = await _dbService.VectorSearchAsync(queryVector, candidateLimit);
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
