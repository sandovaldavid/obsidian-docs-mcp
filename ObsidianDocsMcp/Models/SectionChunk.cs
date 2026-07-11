namespace ObsidianDocsMcp.Models;

public class SectionChunk
{
    public string Id { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;       // Note name or path
    public string Header { get; set; } = string.Empty;      // e.g. "## Configuration"
    public string Content { get; set; } = string.Empty;     // Plain text of the fragment
    public float[]? Embedding { get; set; }
}
