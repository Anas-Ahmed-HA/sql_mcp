namespace SqlMcp.Server.Catalog;

/// <summary>Read access to database metadata, already limited to each database's Allowed views.</summary>
public interface IDatabaseCatalog
{
    /// <summary>The exposed tables and views of one database. Cached per database; safe to call on every request.</summary>
    /// <param name="database">A configured name, as returned by <see cref="Security.IDatabaseAccess.Resolve"/>.</param>
    Task<CatalogSnapshot> GetSnapshotAsync(string database, CancellationToken cancellationToken = default);

    /// <summary>Columns, keys, constraints, relationships and dependencies of one exposed object. Not cached.</summary>
    Task<ObjectSchema> DescribeAsync(string database, CatalogObject obj, CancellationToken cancellationToken = default);
}
