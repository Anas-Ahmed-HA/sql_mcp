using ModelContextProtocol.Server;
using SqlMcp.Server.Metadata;
using SqlMcp.Server.Security;

namespace SqlMcp.Server.Tools;

/// <summary>Database tools: list_databases (one file per tool).</summary>
[McpServerToolType]
public sealed partial class DatabaseTools(IDatabaseAccess access, IMetadataStore metadata)
{
    private readonly IDatabaseAccess _access = access;
    private readonly IMetadataStore _metadata = metadata;
}
