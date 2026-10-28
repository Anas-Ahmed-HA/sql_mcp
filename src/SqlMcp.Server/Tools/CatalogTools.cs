using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SqlMcp.Server.Catalog;
using SqlMcp.Server.Configuration;
using SqlMcp.Server.Metadata;

namespace SqlMcp.Server.Tools;

/// <summary>Metadata tools: list_objects and describe_object (one file per tool).</summary>
[McpServerToolType]
public sealed partial class CatalogTools(IDatabaseCatalog catalog, IMetadataStore metadata, IOptions<DatabaseOptions> options)
{
    private const int MaxListedObjects = 500;

    private readonly IDatabaseCatalog _catalog = catalog;
    private readonly IMetadataStore _metadata = metadata;
    private readonly DatabaseOptions _options = options.Value;

    private static DbObjectKind? ParseKind(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "views" or "view" => DbObjectKind.View,
        "tables" or "table" => DbObjectKind.Table,
        "all" or "" => null,
        _ => throw new McpException($"Unknown kind '{kind}'. Use 'views', 'tables' or 'all'."),
    };

    private static string TypeName(DbObjectKind kind) => kind == DbObjectKind.View ? "view" : "table";
}
