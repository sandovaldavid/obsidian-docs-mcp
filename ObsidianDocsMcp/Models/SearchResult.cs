namespace ObsidianDocsMcp.Models;

public class SearchResult
{
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
    public string SourceType { get; set; } = string.Empty; // "Semantic", "Keyword", or "Hybrid"
}
