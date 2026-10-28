using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SqlMcp.Server.Configuration;
using SqlMcp.Server.Security;

namespace SqlMcp.Server.Query;

/// <param name="Outcome">"ok", "rejected" (by the guard), "error" (from SQL Server) or "cancelled".</param>
public sealed partial record AuditEntry(
    DateTimeOffset Timestamp,
    string Tool,
    CallerInfo Caller,
    string Database,
    string Sql,
    string Outcome,
    string? Detail,
    IReadOnlyList<string> Objects,
    int? Rows,
    bool? Truncated,
    long ElapsedMs);

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry);
}

/// <summary>
/// Appends one JSON line per query to a daily file and logs a one-line summary. A failed write is logged
/// but doesn't fail the query: availability is preferred over blocking on a full disk.
/// </summary>
public sealed partial class JsonFileAuditLog : IAuditLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Arabic text readable in the file
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _directory;
    private readonly ILogger<JsonFileAuditLog> _logger;

    public JsonFileAuditLog(IOptions<QueryOptions> options, IHostEnvironment environment, ILogger<JsonFileAuditLog> logger)
    {
        _directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.AuditDirectory));
        _logger = logger;
        CheckWritable();
    }

    /// <summary>
    /// Writes and deletes a probe file, so a folder the service account can't write to is reported at startup
    /// instead of being noticed only when audit files turn out to be missing.
    /// </summary>
    private void CheckWritable()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var probe = Path.Combine(_directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            _logger.LogInformation("Audit files are written to {Directory}.", _directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                ex,
                "Cannot write audit files to {Directory}. Queries will run without an audit record until the service account can write there (Query:AuditDirectory).",
                _directory);
        }
    }

    public async Task WriteAsync(AuditEntry entry)
    {
        _logger.LogInformation(
            "Audit {Tool} {Outcome} on {Database} from {Ip}: {Rows} rows in {ElapsedMs} ms {Detail}",
            entry.Tool, entry.Outcome, entry.Database, entry.Caller.IpAddress ?? "unknown", entry.Rows ?? 0, entry.ElapsedMs, entry.Detail);

        var path = Path.Combine(_directory, $"audit-{entry.Timestamp:yyyy-MM-dd}.jsonl");
        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;

        await _writeLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(path, line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not write the audit entry to {Path}.", path);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
