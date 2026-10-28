using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Server.Metadata;

namespace SqlMcp.Server.Tools;

// The get_business_rules tool.
public sealed partial class MetadataTools
{
    [McpServerTool(Name = "get_business_rules", Title = "Get business rules and relationships", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns the business-rules document of one database as Markdown: which views are allowed, what each " +
        "column means, business rules, how views join (views have no foreign keys) and other notes. " +
        "Read it before writing queries.")]
    public string GetBusinessRules(
        [Description(ToolDescriptions.Database)]
        string database)
    {
        var db = _access.Resolve(database);
        return _metadata.GetDocument(db)?.Markdown ?? MetadataDocument.MissingMessage(db);
    }
}
