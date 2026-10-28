using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace SqlMcp.Server.Tools;

// The run_select tool.
public sealed partial class QueryTools
{
    [McpServerTool(Name = RunSelectToolName, Title = "Run a read-only SELECT", ReadOnly = true, Idempotent = true, OpenWorld = false)]
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
        var rowLimit = Math.Clamp(maxRows ?? _options.DefaultRows, 1, _options.MaxRows);

        var snapshot = await _catalog.GetSnapshotAsync(cancellationToken);
        var verdict = _guard.Validate(sql, snapshot);
        if (!verdict.IsAllowed)
        {
            await AuditAsync(RunSelectToolName, sql, "rejected", verdict.Error, [], null, null, stopwatch);
            throw new McpException(verdict.Error!);
        }

        var objects = verdict.Objects.Select(o => o.FullName).ToList();
        try
        {
            var result = await _executor.ExecuteAsync(sql, rowLimit, cancellationToken);
            await AuditAsync(RunSelectToolName, sql, "ok", null, objects, result.Rows.Count, result.Truncated, stopwatch);

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
            await AuditAsync(RunSelectToolName, sql, "error", message, objects, null, null, stopwatch);
            throw new McpException(message);
        }
        catch (SqlException ex)
        {
            // SQL Server's messages ("Invalid column name ...", "Conversion failed ...") help the agent fix its query.
            // Passed through unchanged: the guard words hidden columns/objects the same way, so the two can't be told apart.
            await AuditAsync(RunSelectToolName, sql, "error", $"{ex.Number}: {ex.Message}", objects, null, null, stopwatch);
            throw new McpException(ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await AuditAsync(RunSelectToolName, sql, "cancelled", null, objects, null, null, stopwatch);
            throw;
        }
    }
}
