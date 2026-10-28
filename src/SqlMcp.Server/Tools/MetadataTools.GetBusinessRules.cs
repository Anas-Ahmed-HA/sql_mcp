using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Server.Metadata;
using SqlMcp.Server.Resources;

namespace SqlMcp.Server.Tools;

// The get_business_rules tool.
public sealed partial class MetadataTools
{
    [McpServerTool(Name = "get_business_rules", Title = "Get business rules and relationships", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns the business-rules document for this database as Markdown: what each table/view means, how views " +
        "join (views have no foreign keys), code values, counting rules and which data is sensitive. Read it before " +
        "writing queries. Also available as the MCP resource " + MetadataResources.BusinessRulesUri + ".")]
    public string GetBusinessRules() =>
        _metadata.GetDocument()?.Markdown ?? MetadataDocument.MissingMessage(_metadata.ConnectionName);
}
