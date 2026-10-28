using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SqlMcp.Server.Configuration;

namespace SqlMcp.Server.Metadata;

/// <summary>
/// Reads metadata/&lt;database&gt;.md, one file per database. A file is checked on every call and re-parsed only
/// when its timestamp or size changes, so edits apply without a restart and every instance sees the same file.
/// Validation against the database happens in the catalog, which knows the objects.
/// </summary>
public sealed partial class MetadataStore : IMetadataStore
{
    private readonly string _directory;
    private readonly ILogger<MetadataStore> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cached = new(StringComparer.OrdinalIgnoreCase);

    public MetadataStore(IOptions<MetadataOptions> options, IHostEnvironment environment, ILogger<MetadataStore> logger)
    {
        _logger = logger;
        _directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.Directory));
    }

    public MetadataDocument? GetDocument(string database)
    {
        // Database names are validated at startup (letters, digits, _ . -), so this stays inside the folder.
        var path = Path.Combine(_directory, database + ".md");
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return null;
        }

        var cached = _cached.GetValueOrDefault(database);
        if (cached is not null && cached.LastWriteUtc == file.LastWriteTimeUtc && cached.Length == file.Length)
        {
            return cached.Document;
        }

        try
        {
            var document = MetadataDocument.Parse(database, File.ReadAllText(path));
            _cached[database] = new CacheEntry(file.LastWriteTimeUtc, file.Length, document);
            _logger.LogInformation("Loaded metadata {Path}: {Sections} sections.", path, document.Sections.Count);
            return document;
        }
        catch (IOException ex)
        {
            // Most likely the file is being saved right now; serve the previous version until the next call.
            _logger.LogWarning(ex, "Could not read metadata {Path}; using the previous version.", path);
            return cached?.Document;
        }
    }

    private sealed partial record CacheEntry(DateTime LastWriteUtc, long Length, MetadataDocument Document);
}
