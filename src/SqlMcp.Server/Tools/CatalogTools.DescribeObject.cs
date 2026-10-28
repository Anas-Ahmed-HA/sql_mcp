using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SqlMcp.Server.Catalog;

namespace SqlMcp.Server.Tools;

// The describe_object tool.
public sealed partial class CatalogTools
{
    [McpServerTool(Name = "describe_object", Title = "Describe a table or view", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns the schema of one table or view: columns with types and nullability, primary key, unique and " +
        "check constraints, foreign keys in both directions, for views the objects they read from, and the " +
        "object's section of the business-rules document when one exists. Call it before writing a query against the object.")]
    public async Task<DescribeObjectResult> DescribeObjectAsync(
        [Description("Object name as returned by list_objects, e.g. 'dbo.PBI_PH_Assets'. The schema can be omitted when the name is unique.")]
        string name,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _catalog.GetSnapshotAsync(cancellationToken);
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

        var described = await _catalog.DescribeAsync(obj, cancellationToken);
        var document = _metadata.GetDocument();
        var businessMetadata = document?.FindObjectSection(obj.Schema, obj.Name)?.Markdown;

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

        if (document is not null)
        {
            notes.Add("Relationships between objects, code values and counting rules: call get_business_rules.");
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
            businessMetadata,
            notes);
    }
}
