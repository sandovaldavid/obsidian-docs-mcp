namespace ObsidianDocsMcp.Eval;

/// <summary>Serialized output of one `run` invocation; also the input format of `compare`.</summary>
public class EvalRunResult
{
    public string Label { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public string EmbeddingModel { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public double MeanSearchLatencyMs { get; set; }
    public Dictionary<string, double> Metrics { get; set; } = new();
    public List<QueryEvalResult> PerQuery { get; set; } = new();
}

public class QueryEvalResult
{
    public string Id { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public Dictionary<string, double> Metrics { get; set; } = new();

    /// <summary>Top returned fragments as "FilePath :: Header", for eyeballing misses.</summary>
    public List<string> TopResults { get; set; } = new();
}
