using SqlMcp.Server.Catalog;

namespace SqlMcp.Server.Metadata;

/// <param name="Title">Heading text without the leading #s.</param>
/// <param name="Markdown">The heading line plus everything up to the next heading of the same or a higher level.</param>
public sealed partial record MetadataSection(string Title, int Level, string Markdown);

/// <summary>
/// A parsed business-rules file: the full Markdown for agents to read, plus its sections so the part about
/// one object can be returned with that object's schema. Written by people; explains, never enforces.
/// </summary>
public sealed partial class MetadataDocument
{
    private MetadataDocument(string connectionName, string markdown, IReadOnlyList<MetadataSection> sections)
    {
        ConnectionName = connectionName;
        Markdown = markdown;
        Sections = sections;
    }

    public string ConnectionName { get; }

    public string Markdown { get; }

    public IReadOnlyList<MetadataSection> Sections { get; }

    /// <summary>What agents are told when no file exists for the connection.</summary>
    public static string MissingMessage(string connectionName) =>
        $"No business-rules document has been written for '{connectionName}' yet. Rely on list_objects and describe_object.";

    /// <summary>
    /// The first section whose heading names the object: "dbo.PBI_PH_Assets" or "PBI_PH_Assets",
    /// optionally in backticks or brackets. Case-insensitive.
    /// </summary>
    public MetadataSection? FindObjectSection(string schema, string name) =>
        Sections.FirstOrDefault(s => SqlNames.Split(s.Title.Replace("`", string.Empty)) switch
        {
            [var objectName] => Eq(objectName, name),
            [var sectionSchema, var objectName] => Eq(sectionSchema, schema) && Eq(objectName, name),
            _ => false,
        });

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
