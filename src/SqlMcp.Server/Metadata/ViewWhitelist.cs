using SqlMcp.Server.Catalog;

namespace SqlMcp.Server.Metadata;

/// <summary>
/// The Allowed views table as a hard whitelist. Fails closed: with no table (or no file), nothing is allowed.
/// Entries must be schema-qualified ("dbo.PBI_PH_Assets"); any other entry allows nothing and is reported.
/// </summary>
public sealed partial class ViewWhitelist
{
    private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Schema, string Name)> _entries = [];
    private readonly List<string> _invalidEntries = [];

    public ViewWhitelist(IReadOnlyList<string>? entries)
    {
        IsConfigured = entries is not null;
        foreach (var entry in entries ?? [])
        {
            if (SqlNames.Split(entry) is [var schema, var name])
            {
                _allowed.Add(Key(schema, name));
                _entries.Add((schema, name));
            }
            else
            {
                _invalidEntries.Add(entry);
            }
        }
    }

    /// <summary>False when the metadata file or its Allowed views section is missing.</summary>
    public bool IsConfigured { get; }

    public IReadOnlyList<(string Schema, string Name)> Entries => _entries;

    public IReadOnlyList<string> InvalidEntries => _invalidEntries;

    public bool Allows(string schema, string name) => _allowed.Contains(Key(schema, name));

    // A separator that can't appear in identifiers, so "a.b" + "c" never collides with "a" + "b.c".
    private static string Key(string schema, string name) => $"{schema}\u0000{name}";
}
