using System.Text.Json;

namespace ObsidianDocsMcp.Eval;

/// <summary>
/// A set of real queries annotated with the documentation files (and optionally section
/// headers) that a good retriever should return for each of them. Entries are keyed by
/// file path + header prefix — never by chunk IDs, whose positional component shifts
/// whenever the upstream docs change.
/// </summary>
public class GoldenSet
{
    public int Version { get; set; } = 1;
    public DocsSnapshot? DocsSnapshot { get; set; }
    public List<GoldenQuery> Queries { get; set; } = new();

    public static GoldenSet Load(string path)
    {
        var json = File.ReadAllText(path);
        var set = JsonSerializer.Deserialize<GoldenSet>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not parse golden set at {path}.");
        if (set.Queries.Count == 0)
        {
            throw new InvalidOperationException($"Golden set at {path} contains no queries.");
        }
        return set;
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

/// <summary>
/// Pinned snapshots of the doc repos the eval index must be built from, so metric changes
/// reflect code/config changes rather than upstream documentation churn.
/// </summary>
public class DocsSnapshot
{
    public string? UserHelpRef { get; set; }
    public string? DeveloperDocsRef { get; set; }
    public string? UserHelpZipUrl { get; set; }
    public string? DeveloperDocsZipUrl { get; set; }
}

public class GoldenQuery
{
    public string Id { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string? Category { get; set; }
    public List<RelevantDoc> Relevant { get; set; } = new();
}

public class RelevantDoc
{
    /// <summary>Path relative to the doc repo root, e.g. "en/Plugins/Getting started/Build a plugin.md".</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Optional section header; matched as a prefix so "(Part N)" suffixes still count.</summary>
    public string? Header { get; set; }

    /// <summary>Graded relevance for nDCG: 2 = primary source, 1 = useful, 0 = ignored.</summary>
    public int Grade { get; set; } = 2;
}
