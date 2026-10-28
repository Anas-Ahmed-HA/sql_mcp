namespace SqlMcp.Server.Catalog;

/// <summary>Read access to database metadata, already limited to the configured object scope.</summary>
public interface IDatabaseCatalog
{
    /// <summary>The in-scope tables and views. Cached; safe to call on every request.</summary>
    Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Columns, keys, constraints, relationships and dependencies of one in-scope object. Not cached.</summary>
    Task<ObjectSchema> DescribeAsync(CatalogObject obj, CancellationToken cancellationToken = default);
}
