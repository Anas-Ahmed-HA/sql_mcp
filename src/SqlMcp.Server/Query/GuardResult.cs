using SqlMcp.Server.Catalog;

namespace SqlMcp.Server.Query;

/// <summary>The guard's verdict: allowed with the objects the query reads, or rejected with a reason for the agent.</summary>
public sealed partial record GuardResult(bool IsAllowed, string? Error, IReadOnlyList<CatalogObject> Objects)
{
    public static GuardResult Allow(IReadOnlyList<CatalogObject> objects) => new(true, null, objects);

    public static GuardResult Reject(string error) => new(false, error, []);
}
