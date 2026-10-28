using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SqlMcp.Server.Configuration;
using SqlMcp.Server.Data;
using SqlMcp.Server.Metadata;

namespace SqlMcp.Server.Catalog;

/// <summary>
/// Reads metadata from SQL Server's catalog views. An object is exposed only when all of these allow it:
/// the login can see it (SQL Server's metadata visibility), the object scope includes its kind, the metadata
/// file's Allowed views whitelist lists it, and the exclusion lists don't hide it.
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
    private readonly ExclusionPolicy _policy = new(options.Value);

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
        var scope = _options.ObjectScope;

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

        IReadOnlyList<string> ColumnsOf(int id) => columnsById.TryGetValue(id, out var list) ? list : [];

        // Checked against every object, not just the in-scope ones, so an entry for an out-of-scope object isn't flagged.
        foreach (var problem in _policy.FindIneffectiveEntries(rows.Select(r => (r.Schema, r.Name, ColumnsOf(r.Id))).ToList()))
        {
            _logger.LogWarning("Exclusion entry has no effect: {Problem}. Check the spelling; nothing is hidden by it.", problem);
        }

        var whitelist = new ViewWhitelist(document?.AllowedViews);
        CheckWhitelist(whitelist, rows.Select(r => (r.Schema, r.Name)).ToList());

        var objects = new List<CatalogObject>();
        var notAllowed = 0;
        var excludedObjects = 0;
        var hiddenColumns = 0;
        foreach (var row in rows)
        {
            if (!scope.Includes(row.Kind))
            {
                continue;
            }

            if (!whitelist.Allows(row.Schema, row.Name))
            {
                notAllowed++;
                continue;
            }

            if (_policy.ExcludesObject(row.Schema, row.Name))
            {
                excludedObjects++;
                continue;
            }

            var all = ColumnsOf(row.Id);
            var hidden = _policy.HiddenColumns(row.Schema, row.Name, all);
            hiddenColumns += hidden.Count;

            objects.Add(new CatalogObject(
                row.Id, row.Schema, row.Name, row.Kind, row.Description,
                all.Where(c => !hidden.Contains(c)).ToList(), hidden, row.ApproxRows));
        }

        _logger.LogInformation(
            "Catalog loaded: {Count} objects exposed (scope {Scope}; {NotAllowed} not in Allowed views, {ExcludedObjects} excluded, {HiddenColumns} columns hidden).",
            objects.Count, scope, notAllowed, excludedObjects, hiddenColumns);

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
