using SqlMcp.Server.Catalog;
using SqlMcp.Server.Errors;

namespace SqlMcp.Server.Query;

/// <summary>The guard's verdict: allowed with the objects the query reads, or rejected with an error for the agent.</summary>
public sealed partial record GuardResult(bool IsAllowed, AgentError? Error, IReadOnlyList<CatalogObject> Objects)
{
    public static GuardResult Allow(IReadOnlyList<CatalogObject> objects) => new(true, null, objects);

    public static GuardResult Reject(AgentError error) => new(false, error, []);
}
