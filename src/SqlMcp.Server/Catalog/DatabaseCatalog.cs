using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SqlMcp.Server.Configuration;
using SqlMcp.Server.Data;

namespace SqlMcp.Server.Catalog;

/// <summary>
/// Reads metadata from SQL Server's catalog views. Only what the login can see is returned
/// (SQL Server's metadata visibility), and only what the object scope and exclusion lists allow is kept.
/// </summary>
public sealed partial class DatabaseCatalog(
    ISqlConnectionFactory connections,
    IMemoryCache cache,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseCatalog> logger) : IDatabaseCatalog
{
    private const string SnapshotCacheKey = "catalog:snapshot";

    private readonly ISqlConnectionFactory _connections = connections;
    private readonly IMemoryCache _cache = cache;
    private readonly DatabaseOptions _options = options.Value;
    private readonly ILogger<DatabaseCatalog> _logger = logger;
    private readonly ExclusionPolicy _policy = new(options.Value);

    public async Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _cache.GetOrCreateAsync(SnapshotCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CatalogCacheMinutes);
            return await LoadSnapshotAsync(cancellationToken);
        });
        return snapshot!;
    }

    private async Task<CatalogSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken)
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

        var objects = new List<CatalogObject>();
        var excludedObjects = 0;
        var hiddenColumns = 0;
        foreach (var row in rows)
        {
            if (!scope.Includes(row.Kind))
            {
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
            "Catalog loaded: {Count} objects in scope {Scope}; {ExcludedObjects} excluded, {HiddenColumns} columns hidden.",
            objects.Count, scope, excludedObjects, hiddenColumns);
        return new CatalogSnapshot(objects);
    }

    private SqlCommand CreateCommand(SqlConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        return command;
    }
}
