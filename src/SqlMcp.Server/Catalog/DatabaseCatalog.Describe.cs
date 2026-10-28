using System.Data;
using Microsoft.Data.SqlClient;
using SqlMcp.Server.Data;

namespace SqlMcp.Server.Catalog;

// Describing one object: columns, keys, constraints, relationships and dependencies, with hidden parts removed.
public sealed partial class DatabaseCatalog
{
    public async Task<ObjectSchema> DescribeAsync(CatalogObject obj, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var hidden = obj.HiddenColumns;

        // Definitions are SQL text; drop any that mention a hidden column rather than leak its name.
        bool MentionsHidden(string? sql) => sql is not null && hidden.Any(h => sql.Contains(h, StringComparison.OrdinalIgnoreCase));

        await using var connection = await _connections.OpenAsync(cancellationToken);
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
            var inSnapshot = database is null && referencedId is int id && snapshot.TryGet(id, out _);

            // Views not in the Allowed views whitelist don't exist for agents, so they aren't named either.
            if (type == "view" && !inSnapshot)
            {
                continue;
            }

            dependencies.Add(new DependencyInfo(name, type, InScope: inSnapshot));
        }

        return new ObjectSchema(columns, primaryKey, uniqueKeys, checks, foreignKeys, dependencies);
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
