using Microsoft.Data.SqlClient;

namespace SqlMcp.Server.Data;

/// <summary>
/// Single place that opens database connections. Later phases hook per-user
/// session context (SESSION_CONTEXT for row-level security) in here.
/// </summary>
public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default);
}
