using Microsoft.Data.SqlClient;

namespace SqlMcp.Server.Data;

/// <summary>
/// Single place that opens database connections. Later phases hook per-user
/// session context (SESSION_CONTEXT for row-level security) in here.
/// </summary>
public interface ISqlConnectionFactory
{
    /// <summary>Opens a connection with the named database's own connection string.</summary>
    /// <param name="database">A configured name, as returned by <see cref="Security.IDatabaseAccess.Resolve"/>.</param>
    Task<SqlConnection> OpenAsync(string database, CancellationToken cancellationToken = default);
}
