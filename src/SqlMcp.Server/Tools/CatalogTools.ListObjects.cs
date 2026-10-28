using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SqlMcp.Server.Tools;

// The list_objects tool.
public sealed partial class CatalogTools
{
    [McpServerTool(Name = "list_objects", Title = "List tables and views", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the tables and/or views available in one database, with descriptions, column counts and " +
        "approximate row counts for tables. Call describe_object before querying an object.")]
    public async Task<ListObjectsResult> ListObjectsAsync(
        [Description(ToolDescriptions.Database)]
        string database,
        [Description("Which object types to list: 'views', 'tables' or 'all'.")]
        string kind = "all",
        [Description("Only objects in this schema, e.g. 'dbo'.")]
        string? schema = null,
        [Description("Only objects whose name contains this text (case-insensitive).")]
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var requested = ParseKind(kind);
        var snapshot = await _catalog.GetSnapshotAsync(_access.Resolve(database), cancellationToken);
        var matches = snapshot.Objects
            .Where(o => requested is null || o.Kind == requested)
            .Where(o => string.IsNullOrWhiteSpace(schema) || o.Schema.Equals(schema.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(o => string.IsNullOrWhiteSpace(search) || o.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var objects = matches
            .Take(MaxListedObjects)
            .Select(o => new ObjectSummary(o.FullName, TypeName(o.Kind), o.Description, o.ColumnCount, o.ApproxRowCount))
            .ToList();

        return new ListObjectsResult(matches.Count, matches.Count > MaxListedObjects, objects);
    }
}
