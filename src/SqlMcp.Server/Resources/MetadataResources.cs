using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Server.Metadata;

namespace SqlMcp.Server.Resources;

/// <summary>MCP resources: the business-rules document of the current database.</summary>
[McpServerResourceType]
public sealed partial class MetadataResources(IMetadataStore metadata)
{
    public const string BusinessRulesUri = "metadata://business-rules";

    [McpServerResource(UriTemplate = BusinessRulesUri, Name = "business_rules", Title = "Business rules and relationships", MimeType = "text/markdown")]
    [Description("What the tables and views mean, how they join, code values and counting rules for this database. Written by people; explains, does not enforce.")]
    public string GetBusinessRules() =>
        metadata.GetDocument()?.Markdown ?? MetadataDocument.MissingMessage(metadata.ConnectionName);
}
