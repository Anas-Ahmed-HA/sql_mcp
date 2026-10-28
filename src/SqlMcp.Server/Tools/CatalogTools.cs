using System.ComponentModel;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SqlMcp.Server.Catalog;
using SqlMcp.Server.Configuration;

namespace SqlMcp.Server.Tools;

[McpServerToolType]
public sealed class CatalogTools(IDatabaseCatalog catalog, IOptions<DatabaseOptions> options)
{
    private const int MaxListedObjects = 500;

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
        var scope = options.Value.ObjectScope;
        var requested = ParseKind(kind);
        if (requested is { } k && !scope.Includes(k))
        {
            throw new McpException($"This server does not expose {TypeName(k)}s (object scope: {scope}).");
        }

        var snapshot = await catalog.GetSnapshotAsync(cancellationToken);
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

    [McpServerTool(Name = "describe_object", Title = "Describe a table or view", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns the schema of one table or view: columns with types and nullability, primary key, unique and " +
        "check constraints, foreign keys in both directions, and for views the objects they read from. " +
        "Call it before writing a query against the object.")]
    public async Task<DescribeObjectResult> DescribeObjectAsync(
        [Description("Object name as returned by list_objects, e.g. 'dbo.PBI_PH_Assets'. The schema can be omitted when the name is unique.")]
        string name,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await catalog.GetSnapshotAsync(cancellationToken);
        var resolution = snapshot.Resolve(name);

        if (resolution.IsInvalid)
        {
            throw new McpException($"'{name}' is not a valid object name. Use 'schema.object' or 'object'.");
        }

        if (resolution.Match is not { } obj)
        {
            throw resolution.Candidates.Count > 1
                ? new McpException($"'{name}' is ambiguous. Specify the schema: {string.Join(", ", resolution.Candidates.Select(c => c.FullName))}.")
                : new McpException($"Object '{name}' was not found. Use list_objects to see the available objects.");
        }

        var described = await catalog.DescribeAsync(obj, cancellationToken);

        var references = described.ForeignKeys
            .Where(fk => fk.FromObject.Equals(obj.FullName, StringComparison.OrdinalIgnoreCase))
            .Select(fk => new OutgoingForeignKey(fk.Name, fk.FromColumns, fk.ToObject, fk.ToColumns, fk.OnDelete, fk.OnUpdate, fk.Disabled))
            .ToList();

        var referencedBy = described.ForeignKeys
            .Where(fk => fk.ToObject.Equals(obj.FullName, StringComparison.OrdinalIgnoreCase))
            .Select(fk => new IncomingForeignKey(fk.Name, fk.FromObject, fk.FromColumns, fk.ToColumns, fk.Disabled))
            .ToList();

        var notes = new List<string>();
        if (obj.HiddenColumns.Count > 0)
        {
            notes.Add($"{obj.HiddenColumns.Count} column(s) of this object are hidden by server policy and cannot be queried.");
        }

        if (obj.Kind == DbObjectKind.View)
        {
            notes.Add("SQL Server views have no keys or foreign keys; join conditions between views are not stored in the database.");
            if (described.DependsOn.Count == 0)
            {
                notes.Add("The objects this view reads from are not visible to this login (it needs VIEW DEFINITION).");
            }
        }

        return new DescribeObjectResult(
            obj.FullName,
            TypeName(obj.Kind),
            obj.Description,
            obj.ApproxRowCount,
            described.Columns
                .Select(c => new ColumnResult(c.Name, c.Type, c.Nullable, c.Identity, c.Computed, c.Default, c.ComputedDefinition, c.Description))
                .ToList(),
            described.PrimaryKey,
            described.UniqueKeys,
            described.CheckConstraints,
            references,
            referencedBy,
            described.DependsOn,
            notes);
    }

    private static DbObjectKind? ParseKind(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "views" or "view" => DbObjectKind.View,
        "tables" or "table" => DbObjectKind.Table,
        "all" or "" => null,
        _ => throw new McpException($"Unknown kind '{kind}'. Use 'views', 'tables' or 'all'."),
    };

    private static string TypeName(DbObjectKind kind) => kind == DbObjectKind.View ? "view" : "table";
}
