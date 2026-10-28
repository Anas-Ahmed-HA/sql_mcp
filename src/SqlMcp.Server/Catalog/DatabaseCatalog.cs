using System.Data;
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
public sealed class DatabaseCatalog(
    ISqlConnectionFactory connections,
    IMemoryCache cache,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseCatalog> logger) : IDatabaseCatalog
{
    private const string SnapshotCacheKey = "catalog:snapshot";

    private readonly ExclusionPolicy _policy = new(options.Value);

    // Two result sets: objects, then the column names of every object.
    private const string ObjectsSql = """
        SELECT
            o.object_id,
            s.name,
            o.name,
            CASE o.type WHEN 'U' THEN 0 ELSE 1 END AS Kind,
            CAST(ep.value AS nvarchar(4000))       AS Description,
            CASE WHEN o.type = 'U' THEN
                (SELECT SUM(p.rows) FROM sys.partitions AS p WHERE p.object_id = o.object_id AND p.index_id IN (0, 1))
            END                                    AS ApproxRowCount
        FROM sys.objects AS o
        JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        LEFT JOIN sys.extended_properties AS ep
            ON ep.class = 1 AND ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = N'MS_Description'
        WHERE o.type IN ('U', 'V')
          AND o.is_ms_shipped = 0
          AND o.name <> N'sysdiagrams'
        ORDER BY s.name, o.name;

        SELECT c.object_id, c.name
        FROM sys.columns AS c
        JOIN sys.objects AS o ON o.object_id = c.object_id
        WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0
        ORDER BY c.object_id, c.column_id;
        """;

    // One round trip, five result sets: columns, unique keys, check constraints, foreign keys, dependencies.
    private const string DescribeSql = """
        SELECT c.name, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale,
               c.is_nullable, c.is_identity, c.is_computed,
               dc.definition, cc.definition, CAST(ep.value AS nvarchar(4000))
        FROM sys.columns AS c
        LEFT JOIN sys.default_constraints AS dc ON dc.object_id = c.default_object_id
        LEFT JOIN sys.computed_columns AS cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
        LEFT JOIN sys.extended_properties AS ep
            ON ep.class = 1 AND ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = N'MS_Description'
        WHERE c.object_id = @id
        ORDER BY c.column_id;

        SELECT i.name, i.is_primary_key, col.name
        FROM sys.indexes AS i
        JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
        JOIN sys.columns AS col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
        WHERE i.object_id = @id AND i.is_unique = 1 AND i.is_hypothetical = 0
        ORDER BY i.is_primary_key DESC, i.name, ic.key_ordinal;

        SELECT ck.name, col.name, ck.definition, ck.is_disabled
        FROM sys.check_constraints AS ck
        LEFT JOIN sys.columns AS col ON col.object_id = ck.parent_object_id AND col.column_id = ck.parent_column_id
        WHERE ck.parent_object_id = @id
        ORDER BY ck.name;

        SELECT fk.name,
               fk.parent_object_id, ps.name + N'.' + po.name, pc.name,
               fk.referenced_object_id, rs.name + N'.' + ro.name, rc.name,
               fk.delete_referential_action_desc, fk.update_referential_action_desc, fk.is_disabled
        FROM sys.foreign_keys AS fk
        JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.objects AS po ON po.object_id = fk.parent_object_id
        JOIN sys.schemas AS ps ON ps.schema_id = po.schema_id
        JOIN sys.columns AS pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        JOIN sys.objects AS ro ON ro.object_id = fk.referenced_object_id
        JOIN sys.schemas AS rs ON rs.schema_id = ro.schema_id
        JOIN sys.columns AS rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE fk.parent_object_id = @id OR fk.referenced_object_id = @id
        ORDER BY fk.name, fkc.constraint_column_id;

        SELECT DISTINCT
               d.referenced_database_name,
               COALESCE(d.referenced_schema_name, rs.name),
               d.referenced_entity_name,
               d.referenced_id,
               CASE ro.type WHEN 'U' THEN N'table' WHEN 'V' THEN N'view' ELSE LOWER(ro.type_desc) END
        FROM sys.sql_expression_dependencies AS d
        LEFT JOIN sys.objects AS ro ON ro.object_id = d.referenced_id
        LEFT JOIN sys.schemas AS rs ON rs.schema_id = ro.schema_id
        WHERE d.referencing_id = @id
        ORDER BY 1, 2, 3;
        """;

    public async Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await cache.GetOrCreateAsync(SnapshotCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(options.Value.CatalogCacheMinutes);
            return await LoadSnapshotAsync(cancellationToken);
        });
        return snapshot!;
    }

    private async Task<CatalogSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        var scope = options.Value.ObjectScope;

        await using var connection = await connections.OpenAsync(cancellationToken);
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
            logger.LogWarning("Exclusion entry has no effect: {Problem}. Check the spelling; nothing is hidden by it.", problem);
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

        logger.LogInformation(
            "Catalog loaded: {Count} objects in scope {Scope}; {ExcludedObjects} excluded, {HiddenColumns} columns hidden.",
            objects.Count, scope, excludedObjects, hiddenColumns);
        return new CatalogSnapshot(objects);
    }

    public async Task<ObjectSchema> DescribeAsync(CatalogObject obj, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var hidden = obj.HiddenColumns;

        // Definitions are SQL text; drop any that mention a hidden column rather than leak its name.
        bool MentionsHidden(string? sql) => sql is not null && hidden.Any(h => sql.Contains(h, StringComparison.OrdinalIgnoreCase));

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, DescribeSql);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = obj.ObjectId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = new List<ColumnInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            if (hidden.Contains(name))
            {
                continue;
            }

            var computedDefinition = reader.GetStringOrNull(9);
            columns.Add(new ColumnInfo(
                Name: name,
                Type: FormatType(reader.GetString(1), reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4)),
                Nullable: reader.GetBoolean(5),
                Identity: reader.GetBoolean(6),
                Computed: reader.GetBoolean(7),
                Default: reader.GetStringOrNull(8),
                ComputedDefinition: MentionsHidden(computedDefinition) ? null : computedDefinition,
                Description: reader.GetStringOrNull(10)));
        }

        await reader.NextResultAsync(cancellationToken);
        KeyInfo? primaryKey = null;
        var uniqueKeys = new List<KeyInfo>();
        var keyRows = new List<(string Name, bool IsPrimary, string Column)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            keyRows.Add((reader.GetString(0), reader.GetBoolean(1), reader.GetString(2)));
        }

        foreach (var key in keyRows.GroupBy(r => (r.Name, r.IsPrimary)).Where(k => !k.Any(r => hidden.Contains(r.Column))))
        {
            var info = new KeyInfo(key.Key.Name, key.Select(r => r.Column).ToList());
            if (key.Key.IsPrimary)
            {
                primaryKey = info;
            }
            else
            {
                uniqueKeys.Add(info);
            }
        }

        await reader.NextResultAsync(cancellationToken);
        var checks = new List<CheckConstraintInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var check = new CheckConstraintInfo(
                reader.GetString(0), reader.GetStringOrNull(1), reader.GetStringOrNull(2), reader.GetBoolean(3));
            if ((check.Column is null || !hidden.Contains(check.Column)) && !MentionsHidden(check.Definition))
            {
                checks.Add(check);
            }
        }

        await reader.NextResultAsync(cancellationToken);
        var fkRows = new List<(string Name, int FromId, string From, string FromCol, int ToId, string To, string ToCol, string OnDelete, string OnUpdate, bool Disabled)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            fkRows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetString(5), reader.GetString(6),
                reader.GetString(7), reader.GetString(8), reader.GetBoolean(9)));
        }

        // A relationship to a hidden object or through a hidden column would reveal it, so both ends must be visible.
        var foreignKeys = fkRows
            .GroupBy(r => r.Name)
            .Where(g =>
            {
                var first = g.First();
                return snapshot.TryGet(first.FromId, out var from)
                    && snapshot.TryGet(first.ToId, out var to)
                    && !g.Any(r => from.HiddenColumns.Contains(r.FromCol) || to.HiddenColumns.Contains(r.ToCol));
            })
            .Select(g =>
            {
                var first = g.First();
                return new ForeignKeyInfo(
                    g.Key, first.From, g.Select(r => r.FromCol).ToList(), first.To, g.Select(r => r.ToCol).ToList(),
                    first.OnDelete, first.OnUpdate, first.Disabled);
            })
            .ToList();

        await reader.NextResultAsync(cancellationToken);
        var dependencies = new List<DependencyInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var database = reader.GetStringOrNull(0);
            var schema = reader.GetStringOrNull(1);
            var entity = reader.GetString(2);
            var referencedId = reader.GetInt32OrNull(3);

            // Excluded objects must look like they don't exist, so they're not listed as dependencies either.
            if (database is null && _policy.ExcludesObject(schema ?? string.Empty, entity))
            {
                continue;
            }

            var name = string.Join('.', new[] { database, schema, entity }.Where(p => p is not null));
            var type = database is not null ? "cross-database" : reader.GetStringOrNull(4) ?? "unresolved";

            dependencies.Add(new DependencyInfo(
                name, type, InScope: database is null && referencedId is int id && snapshot.TryGet(id, out _)));
        }

        return new ObjectSchema(columns, primaryKey, uniqueKeys, checks, foreignKeys, dependencies);
    }

    private SqlCommand CreateCommand(SqlConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.Value.CommandTimeoutSeconds;
        return command;
    }

    private static string FormatType(string type, short maxLength, byte precision, byte scale) => type switch
    {
        "nvarchar" or "nchar" => maxLength == -1 ? $"{type}(max)" : $"{type}({maxLength / 2})",
        "varchar" or "char" or "varbinary" or "binary" => maxLength == -1 ? $"{type}(max)" : $"{type}({maxLength})",
        "decimal" or "numeric" => $"{type}({precision},{scale})",
        "datetime2" or "time" or "datetimeoffset" => $"{type}({scale})",
        _ => type,
    };
}
