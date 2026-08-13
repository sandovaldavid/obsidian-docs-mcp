using System;
using System.Collections.Generic;
using System.Linq;
using ObsidianDocsMcp.Models;

namespace ObsidianDocsMcp.Services;

/// <summary>
/// Pure rank-fusion logic shared by the MCP search tool and the evaluation harness.
/// </summary>
public static class RankFusion
{
    /// <summary>
    /// Merges two result lists using Reciprocal Rank Fusion (RRF). RRF only decides the merged
    /// *order* — it's not returned to callers, since its magnitude (~1/k) isn't a meaningful
    /// relevance measure on its own. The returned <see cref="SearchResult.MatchPercent"/> is the
    /// best of the underlying cosine-similarity/BM25-derived percentages instead, so callers get
    /// an interpretable 0-100 match quality regardless of which method(s) found the result.
    /// </summary>
    public static List<SearchResult> ReciprocalRankFusion(List<SearchResult> vectorList, List<SearchResult> ftsList, int limit)
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
