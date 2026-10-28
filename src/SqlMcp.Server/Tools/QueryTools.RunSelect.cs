using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using SqlMcp.Server.Catalog;
using SqlMcp.Server.Errors;

namespace SqlMcp.Server.Tools;

// The run_select tool.
public sealed partial class QueryTools
{
    [McpServerTool(Name = RunSelectToolName, Title = "Run a read-only SELECT", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Runs one read-only T-SQL SELECT against the tables/views from list_objects of one database and returns " +
        "the rows. A query can't reference other databases. " +
        "Use it for statistics and exploration: COUNT, SUM, AVG, GROUP BY, TOP n. Write T-SQL (TOP, not LIMIT). " +
        "Only objects from list_objects can be used; call describe_object first and name columns explicitly. " +
        "Prefer aggregates over fetching raw rows: results are capped at maxRows. " +
        "Errors come back as JSON with a code, the message and a fix to apply before trying again.")]
    public async Task<RunSelectResult> RunSelectAsync(
        [Description(ToolDescriptions.Database)]
        string database,
        [Description("A single T-SQL SELECT statement (CTEs allowed).")]
        string sql,
        [Description("Maximum rows to return. Defaults to the server's default; capped by the server's maximum.")]
        int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var rowLimit = Math.Clamp(maxRows ?? _options.DefaultRows, 1, _options.MaxRows);

        string db;
        try
        {
            db = _access.Resolve(database);
        }
        catch (AgentErrorException ex)
        {
            await AuditAsync(RunSelectToolName, database, sql, "rejected", ex.Error, [], null, null, stopwatch);
            throw;
        }

        CatalogSnapshot snapshot;
        try
        {
            snapshot = await _catalog.GetSnapshotAsync(db, cancellationToken);
        }
        catch (AgentErrorException ex)
        {
            // Usually the database can't be reached; the call is still audited.
            await AuditAsync(RunSelectToolName, db, sql, "error", ex.Error, [], null, null, stopwatch);
            throw;
        }

        var verdict = _guard.Validate(sql, snapshot);
        if (verdict.Error is { } rejection)
        {
            await AuditAsync(RunSelectToolName, db, sql, "rejected", rejection, [], null, null, stopwatch);
            throw rejection.ToException();
        }

        var objects = verdict.Objects.Select(o => o.FullName).ToList();
        try
        {
            var result = await _executor.ExecuteAsync(db, sql, rowLimit, cancellationToken);
            await AuditAsync(RunSelectToolName, db, sql, "ok", null, objects, result.Rows.Count, result.Truncated, stopwatch);

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
        catch (SqlException ex)
        {
            // SQL Server's message names the column or value at fault, so it stays as is; the code and fix are added.
            var error = ex.Number == -2 ? AgentErrors.Timeout((int)stopwatch.Elapsed.TotalSeconds) : AgentErrors.FromSqlException(ex);
            await AuditAsync(RunSelectToolName, db, sql, "error", error, objects, null, null, stopwatch);
            throw error.ToException(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await AuditAsync(RunSelectToolName, db, sql, "cancelled", null, objects, null, null, stopwatch);
            throw;
        }
    }
}
