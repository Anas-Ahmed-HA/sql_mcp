using System.Text.RegularExpressions;

namespace SqlMcp.Server.Configuration;

/// <summary>
/// Bound from the "Database" configuration section. Which objects agents may use is decided only by the
/// Allowed views table in each database's metadata file (see metadata/guidelines.md), not here.
/// </summary>
public sealed partial class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Named connection strings, one per database. The key is the name agents pass as <c>database</c> and the
    /// metadata file name (&lt;name&gt;.md). Each should authenticate as a read-only principal that can only
    /// reach its own database (see sql/01_create_readonly_principal.sql).
    /// </summary>
    public Dictionary<string, string> ConnectionStrings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>How long the list of objects is cached before it is re-read from the database.</summary>
    public int CatalogCacheMinutes { get; set; } = 10;

    /// <summary>At least one database, none empty, and every name usable as a file name.</summary>
    public bool IsValid() =>
        ConnectionStrings.Count > 0 &&
        ConnectionStrings.All(c => ValidName().IsMatch(c.Key) && !string.IsNullOrWhiteSpace(c.Value));

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+$")]
    private static partial Regex ValidName();
}
