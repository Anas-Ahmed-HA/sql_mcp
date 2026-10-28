using Microsoft.Extensions.Options;
using SqlMcp.Server.Catalog;
using SqlMcp.Server.Configuration;

namespace SqlMcp.Server.Data;

/// <summary>
/// At startup: checks the connection, warns if the login can do more than read, then loads the catalog so
/// whitelist and metadata warnings appear in the log right away instead of on the first agent call.
/// Runs in the background so it never delays startup, and never stops the server:
/// a database outage should surface as tool errors, not as a server that fails to launch.
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
        try
        {
            await using (var connection = await connections.OpenAsync(stoppingToken))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = Sql;
                command.CommandTimeout = options.Value.CommandTimeoutSeconds;

                await using var reader = await command.ExecuteReaderAsync(stoppingToken);
                await reader.ReadAsync(stoppingToken);

                logger.LogInformation("Connected to {Server}/{Database} as {Login}.", reader.GetString(0), reader.GetString(1), reader.GetString(2));
                if (reader.GetBoolean(3))
                {
                    logger.LogWarning(
                        "This login has write or elevated permissions. Use the read-only principal from sql/01_create_readonly_principal.sql outside local development.");
                }
            }

            await catalog.GetSnapshotAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database startup check failed. The server will keep running.");
        }
    }
}
