using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SqlMcp.Server.Configuration;
using SqlMcp.Server.Data;
using SqlMcp.Server.Metadata;

namespace SqlMcp.Server.Catalog;

/// <summary>
/// Reads metadata from SQL Server's catalog views. An object is exposed only when the login can see it
/// (SQL Server's metadata visibility) and the metadata file's Allowed views whitelist lists it.
/// </summary>
public sealed partial class DatabaseCatalog(
    ISqlConnectionFactory connections,
    IMemoryCache cache,
    IMetadataStore metadata,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseCatalog> logger) : IDatabaseCatalog
{
    private const string SnapshotCacheKey = "catalog:snapshot";

    private readonly ISqlConnectionFactory _connections = connections;
    private readonly IMemoryCache _cache = cache;
    private readonly IMetadataStore _metadata = metadata;
    private readonly DatabaseOptions _options = options.Value;
    private readonly ILogger<DatabaseCatalog> _logger = logger;

    public async Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        // The whitelist lives in the metadata file, which can change at any time. The store hands out the same
        // document instance until the file changes, so a different instance means the snapshot is stale.
        var document = _metadata.GetDocument();
        if (_cache.TryGetValue(SnapshotCacheKey, out SnapshotEntry? cached) && ReferenceEquals(cached!.Source, document))
        {
            return cached.Snapshot;
        }

        var snapshot = await LoadSnapshotAsync(document, cancellationToken);
        _cache.Set(SnapshotCacheKey, new SnapshotEntry(snapshot, document), TimeSpan.FromMinutes(_options.CatalogCacheMinutes));
        return snapshot;
    }

    private async Task<CatalogSnapshot> LoadSnapshotAsync(MetadataDocument? document, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, ObjectsSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<(int Id, string Schema, string Name, DbObjectKind Kind, string? Description, long? ApproxRows)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) == 0 ? DbObjectKind.Table : DbObjectKind.View,
                reader.GetStringOrNull(4),
                reader.GetInt64OrNull(5)));
        }

        await reader.NextResultAsync(cancellationToken);
        var columnsById = new Dictionary<int, List<string>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt32(0);
            if (!columnsById.TryGetValue(id, out var list))
            {
                columnsById[id] = list = [];
            }

            list.Add(reader.GetString(1));
        }

        var whitelist = new ViewWhitelist(document?.AllowedViews);
        CheckWhitelist(whitelist, rows.Select(r => (r.Schema, r.Name)).ToList());

        var objects = rows
            .Where(r => whitelist.Allows(r.Schema, r.Name))
            .Select(r => new CatalogObject(
                r.Id, r.Schema, r.Name, r.Kind, r.Description,
                columnsById.TryGetValue(r.Id, out var columns) ? columns : [], r.ApproxRows))
            .ToList();

        _logger.LogInformation(
            "Catalog loaded: {Count} of {Visible} objects visible to this login are exposed by Allowed views.",
            objects.Count, rows.Count);

        CheckDocumentedColumns(document, objects);
        return new CatalogSnapshot(objects);
    }

    private SqlCommand CreateCommand(SqlConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        return command;
    }

    /// <param name="Source">The metadata document the snapshot's whitelist came from (null when there was none).</param>
    private sealed partial record SnapshotEntry(CatalogSnapshot Snapshot, MetadataDocument? Source);
}
