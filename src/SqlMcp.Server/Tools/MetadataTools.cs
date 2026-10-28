using ModelContextProtocol.Server;
using SqlMcp.Server.Metadata;

namespace SqlMcp.Server.Tools;

/// <summary>Business-rules tools: get_business_rules (one file per tool).</summary>
[McpServerToolType]
public sealed partial class MetadataTools(IMetadataStore metadata)
{
    private readonly IMetadataStore _metadata = metadata;
}
