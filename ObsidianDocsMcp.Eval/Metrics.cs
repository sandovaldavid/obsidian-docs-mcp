using ObsidianDocsMcp.Models;

namespace ObsidianDocsMcp.Eval;

/// <summary>
/// Standard retrieval-quality metrics (Recall@k, Precision@k, MRR, nDCG@k) computed per query
/// against a <see cref="GoldenQuery"/>'s annotated relevant documents. Pure math, unit-tested
/// in ObsidianDocsMcp.Tests.
/// </summary>
public static class Metrics
{
    public static readonly int[] DefaultKs = { 1, 3, 5, 10 };
    public const int NdcgK = 10;

    /// <summary>Normalizes a doc path so annotation and index paths compare equal across OSes.</summary>
    public static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim().TrimStart('/').ToLowerInvariant();

    /// <summary>
    /// A result matches an annotation when the file paths are equal and, if the annotation pins
    /// a header, the result's header starts with it (tolerating the chunker's "(Part N)" suffix).
    /// </summary>
    public static bool IsMatch(RelevantDoc expected, SearchResult actual)
    {
        if (NormalizePath(expected.FilePath) != NormalizePath(actual.FilePath))
        {
            return false;
        }
        return string.IsNullOrEmpty(expected.Header)
            || actual.Header.StartsWith(expected.Header, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Computes all metrics for one query given its ranked results (best first). Only
    /// annotations with grade &gt;= 1 count as relevant for the binary metrics; grades feed nDCG.
    /// </summary>
    public static Dictionary<string, double> Evaluate(GoldenQuery query, IReadOnlyList<SearchResult> ranked, int[]? ks = null)
    {
        ks ??= DefaultKs;
        var relevant = query.Relevant.Where(r => r.Grade >= 1).ToList();
        if (relevant.Count == 0)
        {
            throw new InvalidOperationException($"Query '{query.Id}' has no relevant documents with grade >= 1.");
        }

        // 1-based rank at which each relevant annotation is first matched (null = never).
        // Each annotation contributes to the metrics once, at its best rank, so several chunks
        // of the same file don't inflate recall or nDCG.
        var firstMatchRank = new int?[relevant.Count];
        for (int pos = 0; pos < ranked.Count; pos++)
        {
            for (int r = 0; r < relevant.Count; r++)
            {
                if (firstMatchRank[r] == null && IsMatch(relevant[r], ranked[pos]))
                {
                    firstMatchRank[r] = pos + 1;
                }
            }
        }

        var metrics = new Dictionary<string, double>();
        foreach (var k in ks)
        {
            metrics[$"recall@{k}"] = firstMatchRank.Count(rank => rank.HasValue && rank.Value <= k) / (double)relevant.Count;

            var hits = firstMatchRank.Count(rank => rank.HasValue && rank.Value <= k);
            metrics[$"precision@{k}"] = hits / (double)k;
        }

        double reciprocalRank = 0;
        for (int pos = 0; pos < ranked.Count; pos++)
        {
            if (relevant.Any(r => IsMatch(r, ranked[pos])))
            {
                reciprocalRank = 1.0 / (pos + 1);
                break;
            }
        }
        metrics["mrr"] = reciprocalRank;

        metrics[$"ndcg@{NdcgK}"] = NdcgAtK(relevant, firstMatchRank, NdcgK);
        return metrics;
    }

    /// <summary>Mean of each metric across queries.</summary>
    public static Dictionary<string, double> Aggregate(IReadOnlyCollection<Dictionary<string, double>> perQuery)
    {
        var aggregated = new Dictionary<string, double>();
        if (perQuery.Count == 0)
        {
            return aggregated;
        }
        foreach (var key in perQuery.First().Keys)
        {
            aggregated[key] = perQuery.Average(q => q.GetValueOrDefault(key));
        }
        return aggregated;
    }

    private static double NdcgAtK(IReadOnlyList<RelevantDoc> relevant, int?[] firstMatchRank, int k)
    {
        double dcg = 0;
        for (int r = 0; r < relevant.Count; r++)
        {
            if (firstMatchRank[r] is int rank && rank <= k)
            {
                dcg += (Math.Pow(2, relevant[r].Grade) - 1) / Math.Log2(rank + 1);
            }
        }

        double idcg = 0;
        var idealGrades = relevant.Select(r => r.Grade).OrderByDescending(g => g).Take(k).ToList();
        for (int i = 0; i < idealGrades.Count; i++)
        {
            idcg += (Math.Pow(2, idealGrades[i]) - 1) / Math.Log2(i + 2);
        }

        return idcg == 0 ? 0 : dcg / idcg;
    }
}
