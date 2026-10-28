using Microsoft.Extensions.Options;
using ModelContextProtocol;
using SqlMcp.Server.Catalog;
using SqlMcp.Server.Configuration;

namespace SqlMcp.Server.Data;

/// <summary>
/// At startup, for each database: checks the connection, warns if the login can do more than read, then loads the catalog so
/// whitelist and metadata warnings appear in the log right away instead of on the first agent call.
/// Runs in the background so it never delays startup, and never stops the server:
/// a database outage should surface as tool errors, not as a server that fails to launch. One failing database
/// doesn't stop the others from being checked.
/// </summary>
public sealed partial class DatabaseStartupCheck(
    ISqlConnectionFactory connections,
    IDatabaseCatalog catalog,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseStartupCheck> logger)
    : BackgroundService
{
    private const string Sql = """
        SELECT
            @@SERVERNAME,
            DB_NAME(),
            SUSER_SNAME(),
            CAST(CASE WHEN IS_SRVROLEMEMBER('sysadmin') = 1
                        OR IS_ROLEMEMBER('db_owner') = 1
                        OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'INSERT') = 1
                        OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'UPDATE') = 1
                        OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'DELETE') = 1
                      THEN 1 ELSE 0 END AS bit) AS CanWrite;
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var database in options.Value.ConnectionStrings.Keys)
        {
            try
            {
                await CheckAsync(database, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (McpException)
            {
                // The connection factory has already logged why.
                logger.LogError("Database {Database} is not reachable at startup. Its tools fail until it is; the server keeps running.", database);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Startup check of database {Database} failed. The server will keep running.", database);
            }
        }
    }

    private async Task CheckAsync(string database, CancellationToken stoppingToken)
    {
        await using (var connection = await connections.OpenAsync(database, stoppingToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Sql;
            command.CommandTimeout = options.Value.CommandTimeoutSeconds;

            await using var reader = await command.ExecuteReaderAsync(stoppingToken);
            await reader.ReadAsync(stoppingToken);

            logger.LogInformation(
                "{Database}: connected to {Server}/{SqlDatabase} as {Login}.",
                database, reader.GetString(0), reader.GetString(1), reader.GetString(2));
            if (reader.GetBoolean(3))
            {
                logger.LogWarning(
                    "{Database}: this login has write or elevated permissions. Use the read-only principal from sql/01_create_readonly_principal.sql outside local development.",
                    database);
            }
        }

        await catalog.GetSnapshotAsync(database, stoppingToken);
    }
}
