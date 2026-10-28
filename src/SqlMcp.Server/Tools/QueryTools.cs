using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SqlMcp.Server.Catalog;
using SqlMcp.Server.Configuration;
using SqlMcp.Server.Query;
using SqlMcp.Server.Security;

namespace SqlMcp.Server.Tools;

public sealed record RunSelectResult(
    IReadOnlyList<QueryColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    int RowCount,
    bool Truncated,
    long ElapsedMs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note);

[McpServerToolType]
public sealed class QueryTools(
    IDatabaseCatalog catalog,
    SelectQueryGuard guard,
    SelectQueryExecutor executor,
    IAuditLog audit,
    ICallerContext caller,
    IOptions<QueryOptions> options)
{
    private const string ToolName = "run_select";

    [McpServerTool(Name = ToolName, Title = "Run a read-only SELECT", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Runs one read-only T-SQL SELECT against the tables/views from list_objects and returns the rows. " +
        "Use it for statistics and exploration: COUNT, SUM, AVG, GROUP BY, TOP n. Write T-SQL (TOP, not LIMIT). " +
        "Only objects from list_objects can be used; call describe_object first and name columns explicitly. " +
        "Prefer aggregates over fetching raw rows: results are capped at maxRows.")]
    public async Task<RunSelectResult> RunSelectAsync(
        [Description("A single T-SQL SELECT statement (CTEs allowed).")]
        string sql,
        [Description("Maximum rows to return. Defaults to the server's default; capped by the server's maximum.")]
        int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var limits = options.Value;
        var rowLimit = Math.Clamp(maxRows ?? limits.DefaultRows, 1, limits.MaxRows);

        var snapshot = await catalog.GetSnapshotAsync(cancellationToken);
        var verdict = guard.Validate(sql, snapshot);
        if (!verdict.IsAllowed)
        {
            await AuditAsync(sql, "rejected", verdict.Error, [], null, null, stopwatch);
            throw new McpException(verdict.Error!);
        }

        var objects = verdict.Objects.Select(o => o.FullName).ToList();
        try
        {
            var result = await executor.ExecuteAsync(sql, rowLimit, cancellationToken);
            await AuditAsync(sql, "ok", null, objects, result.Rows.Count, result.Truncated, stopwatch);

            return new RunSelectResult(
                result.Columns,
                result.Rows,
                result.Rows.Count,
                result.Truncated,
                stopwatch.ElapsedMilliseconds,
                result.Truncated
                    ? $"Only the first {rowLimit} rows are returned. Aggregate (COUNT/GROUP BY) or filter with WHERE instead of paging through rows."
                    : null);
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            var message = $"The query timed out after {stopwatch.Elapsed.TotalSeconds:0} s. Filter it more narrowly or aggregate.";
            await AuditAsync(sql, "error", message, objects, null, null, stopwatch);
            throw new McpException(message);
        }
        catch (SqlException ex)
        {
            // SQL Server's messages ("Invalid column name ...", "Conversion failed ...") help the agent fix its query.
            // Passed through unchanged: the guard words hidden columns/objects the same way, so the two can't be told apart.
            await AuditAsync(sql, "error", $"{ex.Number}: {ex.Message}", objects, null, null, stopwatch);
            throw new McpException(ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await AuditAsync(sql, "cancelled", null, objects, null, null, stopwatch);
            throw;
        }
    }

    private Task AuditAsync(string sql, string outcome, string? detail, IReadOnlyList<string> objects, int? rows, bool? truncated, Stopwatch stopwatch) =>
        audit.WriteAsync(new AuditEntry(
            DateTimeOffset.UtcNow, ToolName, caller.Current, sql, outcome, detail, objects, rows, truncated, stopwatch.ElapsedMilliseconds));
}
