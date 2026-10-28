using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SqlMcp.Server.Configuration;
using SqlMcp.Server.Errors;

namespace SqlMcp.Server.Data;

/// <summary>
/// Reports unhealthy when any configured database can't be reached, so a monitor on /health notices a database
/// outage instead of only a stopped process. /health is open, so the response says healthy or not and nothing
/// more; the log names the databases that failed.
/// </summary>
public sealed partial class DatabaseHealthCheck(
    ISqlConnectionFactory connections,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseHealthCheck> logger)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(options.Value.ConnectionStrings.Keys.Select(d => CanReachAsync(d, cancellationToken)));
        return results.All(ok => ok) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
    }

    private async Task<bool> CanReachAsync(string database, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await connections.OpenAsync(database, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (AgentErrorException)
        {
            // The connection factory has already logged why.
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Health check could not reach database {Database}.", database);
            return false;
        }
    }
}
