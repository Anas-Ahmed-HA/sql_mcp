namespace SqlMcp.Server.Configuration;

/// <summary>
/// Bound from the "Database" configuration section. Which objects agents may use is decided only by the
/// Allowed views table in the connection's metadata file (see metadata/guidelines.md), not here.
/// </summary>
public sealed partial class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Named connection strings, one per database (key = logical database name, also the metadata file name).
    /// Phase 0 serves exactly one. Each should authenticate as a read-only principal
    /// (see sql/01_create_readonly_principal.sql).
    /// </summary>
    public Dictionary<string, string> ConnectionStrings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>How long the list of objects is cached before it is re-read from the database.</summary>
    public int CatalogCacheMinutes { get; set; } = 10;

    // Phase 0 serves a single database; options validation guarantees exactly one entry.
    // These are the only places that assumption is made.

    public string ConnectionName => ConnectionStrings.Keys.Single();

    public string ConnectionString => ConnectionStrings.Values.Single();
}
