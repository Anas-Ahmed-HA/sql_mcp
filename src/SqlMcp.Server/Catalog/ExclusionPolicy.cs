using SqlMcp.Server.Configuration;

namespace SqlMcp.Server.Catalog;

/// <summary>
/// The ExcludeObjects / ExcludeColumns settings, normalised for lookup. Matching is case-insensitive,
/// and an entry without a schema matches that name in any schema.
/// </summary>
public sealed class ExclusionPolicy
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

    /// <summary>
    /// Entries that are malformed or match nothing in the database. Each is likely a typo, which would
    /// leave the object or column visible, so the caller should log them loudly.
    /// </summary>
    public IReadOnlyList<string> FindIneffectiveEntries(IReadOnlyCollection<(string Schema, string Name, IReadOnlyList<string> Columns)> allObjects)
    {
        var problems = new List<string>(_invalidEntries);

        problems.AddRange(_objects
            .Where(k => !allObjects.Any(o => k.Matches(o.Schema, o.Name)))
            .Select(k => $"ExcludeObjects: '{k}' matches no table or view"));

        foreach (var (key, column) in _columns)
        {
            var targets = allObjects.Where(o => key is null || key.Value.Matches(o.Schema, o.Name)).ToList();
            if (key is not null && targets.Count == 0)
            {
                problems.Add($"ExcludeColumns: '{key}' matches no table or view");
            }
            else if (!targets.Any(o => o.Columns.Contains(column, Comparer)))
            {
                problems.Add($"ExcludeColumns: column '{column}' not found in '{(key?.ToString() ?? AllObjects)}'");
            }
        }

        return problems.Distinct().ToList();
    }

    private readonly record struct ObjectKey(string? Schema, string Name)
    {
        public static ObjectKey? TryParse(string entry) => SqlNames.Split(entry) switch
        {
            [var name] => new ObjectKey(null, name),
            [var schema, var name] => new ObjectKey(schema, name),
            _ => null,
        };

        public bool Matches(string schema, string name) =>
            Comparer.Equals(Name, name) && (Schema is null || Comparer.Equals(Schema, schema));

        public override string ToString() => Schema is null ? Name : $"{Schema}.{Name}";
    }
}
