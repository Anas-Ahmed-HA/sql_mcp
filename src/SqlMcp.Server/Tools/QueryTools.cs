using System.Diagnostics;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using SqlMcp.Server.Catalog;
using SqlMcp.Server.Configuration;
using SqlMcp.Server.Query;
using SqlMcp.Server.Security;

namespace SqlMcp.Server.Tools;

/// <summary>Data tools: run_select (one file per tool).</summary>
[McpServerToolType]
public sealed partial class QueryTools(
    IDatabaseCatalog catalog,
    SelectQueryGuard guard,
    SelectQueryExecutor executor,
    IAuditLog audit,
    ICallerContext caller,
    IOptions<QueryOptions> options)
{
    private const string RunSelectToolName = "run_select";

    private readonly IDatabaseCatalog _catalog = catalog;
    private readonly SelectQueryGuard _guard = guard;
    private readonly SelectQueryExecutor _executor = executor;
    private readonly IAuditLog _audit = audit;
    private readonly ICallerContext _caller = caller;
    private readonly QueryOptions _options = options.Value;

    private Task AuditAsync(string tool, string sql, string outcome, string? detail, IReadOnlyList<string> objects, int? rows, bool? truncated, Stopwatch stopwatch) =>
        _audit.WriteAsync(new AuditEntry(
            DateTimeOffset.UtcNow, tool, _caller.Current, sql, outcome, detail, objects, rows, truncated, stopwatch.ElapsedMilliseconds));
}
