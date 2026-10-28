using SqlMcp.Server.Catalog;

namespace SqlMcp.Server.Metadata;

/// <param name="Title">Heading text without the leading #s.</param>
/// <param name="Markdown">The heading line plus everything up to the next heading of the same or a higher level.</param>
public sealed partial record MetadataSection(string Title, int Level, string Markdown);

/// <summary>
/// A parsed business-rules file (see metadata/guidelines.md): the full Markdown for agents to read, its sections
/// so the part about one view can be returned with that view's schema, and the Allowed views whitelist.
/// </summary>
public sealed partial class MetadataDocument
{
    public const string AllowedViewsHeading = "Allowed views";
    public const string ColumnsHeading = "Columns";

    private MetadataDocument(string connectionName, string markdown, IReadOnlyList<MetadataSection> sections, string? summary)
    {
        ConnectionName = connectionName;
        Markdown = markdown;
        Sections = sections;
        Summary = summary;
        AllowedViews = ReadAllowedViews();
    }

    public string ConnectionName { get; }

    public string Markdown { get; }

    /// <summary>The text under the "# &lt;database&gt;" title: what the database is for. Null when there is none.</summary>
    public string? Summary { get; }

    public IReadOnlyList<MetadataSection> Sections { get; }

    /// <summary>
    /// The first column of the "## Allowed views" table, as written. Null when the section is missing,
    /// empty when it has no rows. Enforced as a whitelist by the catalog (see <see cref="ViewWhitelist"/>).
    /// </summary>
    public IReadOnlyList<string>? AllowedViews { get; }

    /// <summary>What agents are told when no file exists for the connection.</summary>
    public static string MissingMessage(string connectionName) =>
        $"No business-rules document has been written for '{connectionName}' yet. Rely on list_objects and describe_object.";

    /// <summary>
    /// The first section whose heading names the object: "dbo.PBI_PH_Assets" or "PBI_PH_Assets",
    /// optionally in backticks or brackets. Case-insensitive.
    /// </summary>
    public MetadataSection? FindObjectSection(string schema, string name) =>
        IndexOfObjectSection(schema, name) is var index and >= 0 ? Sections[index] : null;

    /// <summary>
    /// The first column of the object's "### Columns" table. Null when the object has no section or the
    /// section has no Columns subsection.
    /// </summary>
    public IReadOnlyList<string>? ColumnsOf(string schema, string name)
    {
        var index = IndexOfObjectSection(schema, name);
        if (index < 0)
        {
            return null;
        }

        var columns = ChildrenOf(index).FirstOrDefault(s => Eq(s.Title, ColumnsHeading));
        return columns is null ? null : FirstTableColumn(columns.Markdown);
    }

    private int IndexOfObjectSection(string schema, string name)
    {
        for (var i = 0; i < Sections.Count; i++)
        {
            if (ObjectName.Parse(Sections[i].Title) is { } heading && heading.Matches(schema, name))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The sections nested under the one at <paramref name="index"/>.</summary>
    private IEnumerable<MetadataSection> ChildrenOf(int index)
    {
        var level = Sections[index].Level;
        for (var i = index + 1; i < Sections.Count && Sections[i].Level > level; i++)
        {
            yield return Sections[i];
        }
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
