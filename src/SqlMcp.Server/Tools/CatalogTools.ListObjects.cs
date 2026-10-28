using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SqlMcp.Server.Catalog;

namespace SqlMcp.Server.Tools;

// The list_objects tool.
public sealed partial class CatalogTools
{
    [McpServerTool(Name = "list_objects", Title = "List tables and views", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the tables and/or views available on this server, with descriptions, column counts and " +
        "approximate row counts for tables. Start here, then call describe_object before querying an object.")]
    public async Task<ListObjectsResult> ListObjectsAsync(
        [Description("Which object types to list: 'views', 'tables' or 'all'. Limited to what this server exposes.")]
        string kind = "all",
        [Description("Only objects in this schema, e.g. 'dbo'.")]
        string? schema = null,
        [Description("Only objects whose name contains this text (case-insensitive).")]
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var scope = _options.ObjectScope;
        var requested = ParseKind(kind);
        if (requested is { } k && !scope.Includes(k))
        {
            throw new McpException($"This server does not expose {TypeName(k)}s (object scope: {scope}).");
        }

        var snapshot = await _catalog.GetSnapshotAsync(cancellationToken);
        var matches = snapshot.Objects
            .Where(o => requested is null || o.Kind == requested)
            .Where(o => string.IsNullOrWhiteSpace(schema) || o.Schema.Equals(schema.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(o => string.IsNullOrWhiteSpace(search) || o.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var objects = matches
            .Take(MaxListedObjects)
            .Select(o => new ObjectSummary(o.FullName, TypeName(o.Kind), o.Description, o.ColumnCount, o.ApproxRowCount))
            .ToList();

        return new ListObjectsResult(scope.ToString(), matches.Count, matches.Count > MaxListedObjects, objects);
    }
}
