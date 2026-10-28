using System.Data;
using Microsoft.Data.SqlClient;
using SqlMcp.Server.Data;

namespace SqlMcp.Server.Catalog;

// Describing one object: columns, keys, constraints, relationships and dependencies.
public sealed partial class DatabaseCatalog
{
    public async Task<ObjectSchema> DescribeAsync(string database, CatalogObject obj, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(database, cancellationToken);

        await using var connection = await _connections.OpenAsync(database, cancellationToken);
        await using var command = CreateCommand(connection, DescribeSql);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = obj.ObjectId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = new List<ColumnInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnInfo(
                Name: reader.GetString(0),
                Type: FormatType(reader.GetString(1), reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4)),
                Nullable: reader.GetBoolean(5),
                Identity: reader.GetBoolean(6),
                Computed: reader.GetBoolean(7),
                Default: reader.GetStringOrNull(8),
                ComputedDefinition: reader.GetStringOrNull(9),
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

        foreach (var key in keyRows.GroupBy(r => (r.Name, r.IsPrimary)))
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
            checks.Add(new CheckConstraintInfo(
                reader.GetString(0), reader.GetStringOrNull(1), reader.GetStringOrNull(2), reader.GetBoolean(3)));
        }

        await reader.NextResultAsync(cancellationToken);
        var fkRows = new List<(string Name, int FromId, string From, string FromCol, int ToId, string To, string ToCol, string OnDelete, string OnUpdate, bool Disabled)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            fkRows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetString(5), reader.GetString(6),
                reader.GetString(7), reader.GetString(8), reader.GetBoolean(9)));
        }

        // A relationship to an object that isn't exposed would reveal it, so both ends must be in the snapshot.
        var foreignKeys = fkRows
            .Where(r => snapshot.TryGet(r.FromId, out _) && snapshot.TryGet(r.ToId, out _))
            .GroupBy(r => r.Name)
            .Select(g =>
            {
                var first = g.First();
                return new ForeignKeyInfo(
                    g.Key, first.From, g.Select(r => r.FromCol).ToList(), first.To, g.Select(r => r.ToCol).ToList(),
                    first.OnDelete, first.OnUpdate, first.Disabled);
            })
            .ToList();

        // Same rule for dependencies: only exposed objects are named.
        await reader.NextResultAsync(cancellationToken);
        var dependencies = new List<DependencyInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (snapshot.TryGet(reader.GetInt32(0), out var dependency))
            {
                dependencies.Add(new DependencyInfo(dependency.FullName, dependency.Kind == DbObjectKind.View ? "view" : "table"));
            }
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
