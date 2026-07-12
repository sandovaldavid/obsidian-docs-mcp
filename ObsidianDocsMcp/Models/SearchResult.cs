namespace ObsidianDocsMcp.Models;

public class SearchResult
{
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// How well this fragment matches the query, as a 0-100 percentage — the better of the raw
    /// cosine similarity (semantic) and BM25-derived (keyword) scores, not the internal
    /// Reciprocal Rank Fusion value used only to order results.
    /// </summary>
    public double MatchPercent { get; set; }
    public string SourceType { get; set; } = string.Empty; // "Semantic", "Keyword", or "Hybrid"
}
