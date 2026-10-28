using Microsoft.Extensions.Options;
using SqlMcp.Server.Configuration;
using SqlMcp.Server.Data;

namespace SqlMcp.Server.Query;

public sealed partial record QueryColumn(string Name, string Type);

public sealed partial record QueryResult(IReadOnlyList<QueryColumn> Columns, IReadOnlyList<object?[]> Rows, bool Truncated);

/// <summary>Runs SQL that the guard has already approved. Never call it with unvalidated text.</summary>
public sealed partial class SelectQueryExecutor(
    ISqlConnectionFactory connections,
    IOptions<DatabaseOptions> database,
    IOptions<QueryOptions> query)
{
    private readonly ISqlConnectionFactory _connections = connections;
    private readonly DatabaseOptions _database = database.Value;
    private readonly QueryOptions _query = query.Value;

    public async Task<QueryResult> ExecuteAsync(string validatedSql, int maxRows, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = validatedSql;
        command.CommandTimeout = _database.CommandTimeoutSeconds;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(i => new QueryColumn(
                string.IsNullOrEmpty(reader.GetName(i)) ? $"column{i + 1}" : reader.GetName(i),
                reader.GetDataTypeName(i)))
            .ToList();

        var rows = new List<object?[]>();
        var truncated = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count == maxRows)
            {
                // Stop the server from streaming the rest of a large result we won't return.
                truncated = true;
                command.Cancel();
                break;
            }

            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = ToJsonValue(reader.GetValue(i), _query.MaxCellLength);
            }

            rows.Add(row);
        }

        return new QueryResult(columns, rows, truncated);
    }
}
