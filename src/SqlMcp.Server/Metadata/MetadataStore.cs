using Microsoft.Extensions.Options;
using SqlMcp.Server.Configuration;

namespace SqlMcp.Server.Metadata;

/// <summary>
/// Reads metadata/&lt;ConnectionName&gt;.md. The file is checked on every call and re-parsed only when its
/// timestamp or size changes, so edits apply without a restart and every instance sees the same file.
/// Validation against the database happens in the catalog, which knows the objects.
/// </summary>
public sealed partial class MetadataStore : IMetadataStore
{
    private readonly string _path;
    private readonly ILogger<MetadataStore> _logger;
    private volatile CacheEntry? _cached;

    public MetadataStore(
        IOptions<MetadataOptions> options,
        IOptions<DatabaseOptions> database,
        IHostEnvironment environment,
        ILogger<MetadataStore> logger)
    {
        _logger = logger;
        ConnectionName = database.Value.ConnectionName;
        var directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.Directory));
        _path = Path.Combine(directory, ConnectionName + ".md");
    }

    public string ConnectionName { get; }

    public MetadataDocument? GetDocument()
    {
        var file = new FileInfo(_path);
        if (!file.Exists)
        {
            return null;
        }

        var cached = _cached;
        if (cached is not null && cached.LastWriteUtc == file.LastWriteTimeUtc && cached.Length == file.Length)
        {
            return cached.Document;
        }

        try
        {
            var document = MetadataDocument.Parse(ConnectionName, File.ReadAllText(_path));
            _cached = new CacheEntry(file.LastWriteTimeUtc, file.Length, document);
            _logger.LogInformation("Loaded metadata {Path}: {Sections} sections.", _path, document.Sections.Count);
            return document;
        }
        catch (IOException ex)
        {
            // Most likely the file is being saved right now; serve the previous version until the next call.
            _logger.LogWarning(ex, "Could not read metadata {Path}; using the previous version.", _path);
            return cached?.Document;
        }
    }

    private sealed partial record CacheEntry(DateTime LastWriteUtc, long Length, MetadataDocument Document);
}
