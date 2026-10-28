using Microsoft.Extensions.Options;
using SqlMcp.Server.Configuration;
using SqlMcp.Server.Data;

namespace SqlMcp.Server.Query;

public sealed record QueryColumn(string Name, string Type);

public sealed record QueryResult(IReadOnlyList<QueryColumn> Columns, IReadOnlyList<object?[]> Rows, bool Truncated);

/// <summary>Runs SQL that the guard has already approved. Never call it with unvalidated text.</summary>
public sealed class SelectQueryExecutor(
    ISqlConnectionFactory connections,
    IOptions<DatabaseOptions> database,
    IOptions<QueryOptions> query)
{
    public async Task<QueryResult> ExecuteAsync(string validatedSql, int maxRows, CancellationToken cancellationToken)
    {
        var maxCellLength = query.Value.MaxCellLength;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = validatedSql;
        command.CommandTimeout = database.Value.CommandTimeoutSeconds;

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
                row[i] = ToJsonValue(reader.GetValue(i), maxCellLength);
            }

            rows.Add(row);
        }

        return new QueryResult(columns, rows, truncated);
    }

    private static object? ToJsonValue(object value, int maxCellLength) => value switch
    {
        DBNull => null,
        string s => s.Length > maxCellLength ? string.Concat(s.AsSpan(0, maxCellLength), "…") : s,
        bool or byte or short or int or long or float or double or decimal => value,
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd") : d.ToString("yyyy-MM-ddTHH:mm:ss.FFF"),
        DateTimeOffset o => o.ToString("O"),
        TimeSpan t => t.ToString(),
        Guid g => g.ToString(),
        byte[] b => $"<binary, {b.Length} bytes>",
        _ => value.ToString(),
    };
}
