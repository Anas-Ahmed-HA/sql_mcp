using ModelContextProtocol.Server;
using SqlMcp.Server.Metadata;
using SqlMcp.Server.Security;

namespace SqlMcp.Server.Tools;

/// <summary>Business-rules tools: get_business_rules (one file per tool).</summary>
[McpServerToolType]
public sealed partial class MetadataTools(IDatabaseAccess access, IMetadataStore metadata)
{
    private readonly IDatabaseAccess _access = access;
    private readonly IMetadataStore _metadata = metadata;
}
