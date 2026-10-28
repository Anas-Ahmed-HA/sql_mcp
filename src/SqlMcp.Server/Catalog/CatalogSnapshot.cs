namespace SqlMcp.Server.Catalog;

/// <summary>
/// The in-scope, non-excluded objects of one database at a point in time. Anything else is never loaded,
/// so an object not found here simply does not exist as far as agents are concerned.
/// </summary>
public sealed partial class CatalogSnapshot
{
    private readonly Dictionary<int, CatalogObject> _byId;

    public CatalogSnapshot(IReadOnlyList<CatalogObject> objects)
    {
        Objects = objects;
        _byId = objects.ToDictionary(o => o.ObjectId);
    }

    public IReadOnlyList<CatalogObject> Objects { get; }

    public bool TryGet(int objectId, out CatalogObject obj) => _byId.TryGetValue(objectId, out obj!);

    /// <summary>
    /// Resolves "name", "schema.name", "[schema].[name]" or "\"schema\".\"name\"". Matching is
    /// case-insensitive, like SQL Server's default collation. Database-qualified names are rejected.
    /// </summary>
    public NameResolution Resolve(string name)
    {
        var parts = SqlNames.Split(name);
        return parts switch
        {
            [var objectName] => Resolve(null, objectName),
            [var schema, var objectName] => Resolve(schema, objectName),
            _ => NameResolution.Invalid,
        };
    }

    /// <summary>Resolves an already-split name; a null schema matches the name in any schema.</summary>
    public NameResolution Resolve(string? schema, string name)
    {
        var candidates = Objects
            .Where(o => Eq(o.Name, name) && (schema is null || Eq(o.Schema, schema)))
            .ToList();

        return new NameResolution(candidates.Count == 1 ? candidates[0] : null, candidates, IsInvalid: false);
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <param name="Match">The single matching object, or null when there are zero or several candidates.</param>
public sealed partial record NameResolution(CatalogObject? Match, IReadOnlyList<CatalogObject> Candidates, bool IsInvalid)
{
    public static NameResolution Invalid { get; } = new(null, [], IsInvalid: true);
}
