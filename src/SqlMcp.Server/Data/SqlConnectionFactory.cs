using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SqlMcp.Server.Configuration;
using SqlMcp.Server.Errors;

namespace SqlMcp.Server.Data;

public sealed partial class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly Dictionary<string, string> _connectionStrings;
    private readonly ILogger<SqlConnectionFactory> _logger;

    public SqlConnectionFactory(IOptions<DatabaseOptions> options, ILogger<SqlConnectionFactory> logger)
    {
        _logger = logger;
        _connectionStrings = options.Value.ConnectionStrings.ToDictionary(
            c => c.Key,
            c => new SqlConnectionStringBuilder(c.Value)
            {
                // Shows up in sys.dm_exec_sessions and audit logs, so DBAs can tell MCP traffic apart.
                ApplicationName = "AI_SqlMCP",
                ApplicationIntent = ApplicationIntent.ReadOnly,
            }.ConnectionString,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<SqlConnection> OpenAsync(string database, CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(_connectionStrings[database]);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch (SqlException ex)
        {
            await connection.DisposeAsync();

            // The real reason (login failed, server down) is for the log only. Agents get a plain error, which
            // they can act on (try later, use another database) and which doesn't describe the server.
            _logger.LogWarning("Could not open database {Database}: {Error}", database, ex.Message);
            throw AgentErrors.DatabaseUnavailable(database).ToException(ex);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
