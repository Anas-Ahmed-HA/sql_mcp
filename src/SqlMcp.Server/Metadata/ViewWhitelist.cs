using SqlMcp.Server.Catalog;

namespace SqlMcp.Server.Metadata;

/// <summary>
/// The Allowed views table as a hard whitelist, and the only access control in the server. Fails closed: with
/// no table (or no file), nothing is allowed. Entries must be schema-qualified ("dbo.PBI_PH_Assets"); any other
/// entry allows nothing and is reported.
/// </summary>
public sealed partial class ViewWhitelist
{
    private readonly List<ObjectName> _entries = [];
    private readonly List<string> _invalidEntries = [];

    public ViewWhitelist(IReadOnlyList<string>? entries)
    {
        IsConfigured = entries is not null;
        foreach (var entry in entries ?? [])
        {
            if (ObjectName.Parse(entry) is { IsQualified: true } name)
            {
                _entries.Add(name);
            }
            else
            {
                _invalidEntries.Add(entry);
            }
        }
    }

    /// <summary>False when the metadata file or its Allowed views section is missing.</summary>
    public bool IsConfigured { get; }

    public IReadOnlyList<ObjectName> Entries => _entries;

    public IReadOnlyList<string> InvalidEntries => _invalidEntries;

    public bool Allows(string schema, string name) => _entries.Any(e => e.Matches(schema, name));
}
