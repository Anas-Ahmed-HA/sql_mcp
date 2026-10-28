using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlMcp.Server.Configuration;

namespace SqlMcp.Server.Data;

/// <summary>
/// Verifies connectivity once at startup and warns if the login can do more than read.
/// Runs in the background so it never delays startup, and never stops the server:
/// a database outage should surface as tool errors, not as a server that fails to launch.
/// </summary>
public sealed class DatabaseStartupCheck(
    ISqlConnectionFactory connections,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseStartupCheck> logger)
    : BackgroundService
{
    private const string Sql = """
        SELECT
            @@SERVERNAME                                              AS ServerName,
            DB_NAME()                                                 AS DatabaseName,
            SUSER_SNAME()                                             AS LoginName,
            (SELECT COUNT(*) FROM sys.views WHERE is_ms_shipped = 0)  AS VisibleViews,
            CAST(IS_SRVROLEMEMBER('sysadmin') AS bit)                 AS IsSysadmin,
            CAST(IS_ROLEMEMBER('db_owner') AS bit)                    AS IsDbOwner,
            CAST(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'INSERT') AS bit) AS CanInsert,
            CAST(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'UPDATE') AS bit) AS CanUpdate,
            CAST(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'DELETE') AS bit) AS CanDelete;
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var connection = await connections.OpenAsync(stoppingToken);
            await using var command = connection.CreateCommand();
            command.CommandText = Sql;
            command.CommandTimeout = options.Value.CommandTimeoutSeconds;

            await using var reader = await command.ExecuteReaderAsync(stoppingToken);
            await reader.ReadAsync(stoppingToken);

            logger.LogInformation(
                "Connected to {Server}/{Database} as {Login}; {ViewCount} views visible to this login.",
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3));

            bool canWrite = Enumerable.Range(4, 5).Any(i => !reader.IsDBNull(i) && reader.GetBoolean(i));
            if (canWrite)
            {
                logger.LogWarning(
                    "This login has write or elevated permissions. Use the read-only principal from sql/01_create_readonly_principal.sql outside local development.");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database connectivity check failed. The server will keep running.");
        }
    }
}
