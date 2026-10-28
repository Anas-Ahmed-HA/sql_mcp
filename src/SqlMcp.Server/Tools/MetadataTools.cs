using ModelContextProtocol.Server;
using SqlMcp.Server.Metadata;

namespace SqlMcp.Server.Tools;

/// <summary>
/// Business-rules tools: get_business_rules (one file per tool). The same document is also an MCP resource;
/// the tool exists because many agents can call tools but not read resources.
/// </summary>
[McpServerToolType]
public sealed partial class MetadataTools(IMetadataStore metadata)
{
    private readonly IMetadataStore _metadata = metadata;
}
