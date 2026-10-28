namespace SqlMcp.Server.Configuration;

/// <summary>Bound from the "Query" configuration section; limits for run_select.</summary>
public sealed class QueryOptions
{
    public const string SectionName = "Query";

    /// <summary>Rows returned when the agent doesn't ask for a specific number.</summary>
    public int DefaultRows { get; set; } = 100;

    /// <summary>Hard cap on rows per query, whatever the agent asks for.</summary>
    public int MaxRows { get; set; } = 1000;

    /// <summary>Longer text values are cut to this many characters.</summary>
    public int MaxCellLength { get; set; } = 1000;

    /// <summary>Where audit files (audit-yyyy-MM-dd.jsonl) are written; relative paths are under the app folder.</summary>
    public string AuditDirectory { get; set; } = "logs";
}
