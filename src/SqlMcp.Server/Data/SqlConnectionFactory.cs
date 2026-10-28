using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SqlMcp.Server.Configuration;

namespace SqlMcp.Server.Data;

public sealed partial class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IOptions<DatabaseOptions> options)
    {
        // Phase 0 serves a single database; options validation guarantees exactly one entry.
        var builder = new SqlConnectionStringBuilder(options.Value.ConnectionStrings.Values.Single())
        {
            // Shows up in sys.dm_exec_sessions and audit logs, so DBAs can tell MCP traffic apart.
            ApplicationName = "AI_SqlMCP",
            ApplicationIntent = ApplicationIntent.ReadOnly,
        };
        _connectionString = builder.ConnectionString;
    }

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
