using SqlMcp.Server.Configuration;

namespace SqlMcp.Server.Catalog;

/// <summary>
/// The ExcludeObjects / ExcludeColumns settings, normalised for lookup. Matching is case-insensitive,
/// and an entry without a schema matches that name in any schema.
/// </summary>
public sealed partial class ExclusionPolicy
{
    private const string AllObjects = "*";
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private readonly List<ObjectKey> _objects = [];
    private readonly List<(ObjectKey? Object, string Column)> _columns = [];
    private readonly List<string> _invalidEntries = [];

    public ExclusionPolicy(DatabaseOptions options)
    {
        foreach (var entry in options.ExcludeObjects)
        {
            if (ObjectKey.TryParse(entry) is { } key)
            {
                _objects.Add(key);
            }
            else
            {
                _invalidEntries.Add($"ExcludeObjects: '{entry}'");
            }
        }

        foreach (var (objectEntry, columns) in options.ExcludeColumns)
        {
            ObjectKey? key = null;
            if (objectEntry.Trim() != AllObjects)
            {
                key = ObjectKey.TryParse(objectEntry);
                if (key is null)
                {
                    _invalidEntries.Add($"ExcludeColumns: '{objectEntry}'");
                    continue;
                }
            }

            foreach (var column in columns.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                _columns.Add((key, column.Trim().Trim('[', ']', '"')));
            }
        }
    }

    public bool IsEmpty => _objects.Count == 0 && _columns.Count == 0;

    public bool ExcludesObject(string schema, string name) => _objects.Any(k => k.Matches(schema, name));

    /// <summary>The columns of this object that must be hidden, limited to columns it actually has.</summary>
    public IReadOnlySet<string> HiddenColumns(string schema, string name, IEnumerable<string> columns)
    {
        var hidden = _columns
            .Where(c => c.Object is null || c.Object.Value.Matches(schema, name))
            .Select(c => c.Column)
            .ToHashSet(Comparer);

        return columns.Where(hidden.Contains).ToHashSet(Comparer);
    }
}
