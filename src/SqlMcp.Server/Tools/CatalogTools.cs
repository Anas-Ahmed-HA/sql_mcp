using ModelContextProtocol.Server;
using SqlMcp.Server.Catalog;
using SqlMcp.Server.Errors;
using SqlMcp.Server.Metadata;
using SqlMcp.Server.Security;

namespace SqlMcp.Server.Tools;

/// <summary>Catalog tools: list_objects and describe_object (one file per tool).</summary>
[McpServerToolType]
public sealed partial class CatalogTools(IDatabaseAccess access, IDatabaseCatalog catalog, IMetadataStore metadata)
{
    private const int MaxListedObjects = 500;

    private readonly IDatabaseAccess _access = access;
    private readonly IDatabaseCatalog _catalog = catalog;
    private readonly IMetadataStore _metadata = metadata;

    private static DbObjectKind? ParseKind(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "views" or "view" => DbObjectKind.View,
        "tables" or "table" => DbObjectKind.Table,
        "all" or "" => null,
        _ => throw AgentErrors.InvalidArgument($"Unknown kind '{kind}'.", "Use 'views', 'tables' or 'all'.").ToException(),
    };

    private static string TypeName(DbObjectKind kind) => kind == DbObjectKind.View ? "view" : "table";
}
