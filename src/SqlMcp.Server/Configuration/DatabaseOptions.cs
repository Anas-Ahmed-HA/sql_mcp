namespace SqlMcp.Server.Configuration;

/// <summary>Bound from the "Database" configuration section.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Named connection strings, one per database (key = logical database name).
    /// Phase 0 serves exactly one. Each should authenticate as a read-only principal
    /// (see sql/01_create_readonly_principal.sql).
    /// </summary>
    public Dictionary<string, string> ConnectionStrings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Which object types the MCP exposes at all. Set by whoever hosts the server; agents cannot change it.
    /// Every tool applies it, independent of what the database login itself could read.
    /// </summary>
    public ObjectScope ObjectScope { get; set; } = ObjectScope.Views;

    /// <summary>
    /// Objects hidden from agents even when inside the scope: "schema.object", or "object" for any schema.
    /// Hidden objects are indistinguishable from objects that don't exist.
    /// </summary>
    public string[] ExcludeObjects { get; set; } = [];

    /// <summary>
    /// Columns hidden from agents, keyed by "schema.object" (or "object" for any schema, or "*" for every object).
    /// Hidden columns never appear in metadata and cannot be queried.
    /// </summary>
    public Dictionary<string, string[]> ExcludeColumns { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>How long the list of objects is cached before it is re-read from the database.</summary>
    public int CatalogCacheMinutes { get; set; } = 10;
}

public enum ObjectScope
{
    Views,
    Tables,
    All,
}
