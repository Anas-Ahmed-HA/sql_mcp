namespace SqlMcp.Server.Catalog;

/// <summary>
/// The exposed objects of one database at a point in time. Anything else is never loaded, so an object not
/// found here simply does not exist as far as agents are concerned.
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

    /// <summary>Resolves a name as an agent wrote it; database-qualified or malformed names are invalid.</summary>
    public NameResolution Resolve(string name) =>
        ObjectName.Parse(name) is { } parsed ? Resolve(parsed) : NameResolution.Invalid;

    public NameResolution Resolve(ObjectName name)
    {
        var candidates = Objects.Where(o => name.Matches(o.Schema, o.Name)).ToList();
        return new NameResolution(candidates.Count == 1 ? candidates[0] : null, candidates, IsInvalid: false);
    }
}

/// <param name="Match">The single matching object, or null when there are zero or several candidates.</param>
public sealed partial record NameResolution(CatalogObject? Match, IReadOnlyList<CatalogObject> Candidates, bool IsInvalid)
{
    public static NameResolution Invalid { get; } = new(null, [], IsInvalid: true);
}
