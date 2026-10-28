using SqlMcp.Server.Metadata;

namespace SqlMcp.Server.Catalog;

// All checks that keep the metadata file consistent with the database (see metadata/guidelines.md).
// Logged at each catalog load, so mistakes show up at startup and after every edit of the file.
public sealed partial class DatabaseCatalog
{
    private void CheckWhitelist(ViewWhitelist whitelist, IReadOnlyList<(string Schema, string Name)> visibleObjects)
    {
        if (!whitelist.IsConfigured)
        {
            _logger.LogError(
                "No '## {Heading}' table in metadata/{Connection}.md (or no file). No objects are exposed until it exists; see metadata/guidelines.md.",
                MetadataDocument.AllowedViewsHeading, _metadata.ConnectionName);
            return;
        }

        foreach (var entry in whitelist.InvalidEntries)
        {
            _logger.LogWarning("Allowed views entry '{Entry}' is not schema-qualified (schema.view); it allows nothing.", entry);
        }

        foreach (var entry in whitelist.Entries.Where(e => !visibleObjects.Any(o => e.Matches(o.Schema, o.Name))))
        {
            _logger.LogWarning("Allowed views entry '{Entry}' matches no object visible to this login.", entry);
        }
    }

    private void CheckDocumentedColumns(MetadataDocument? document, IReadOnlyList<CatalogObject> exposed)
    {
        if (document is null)
        {
            return;
        }

        foreach (var obj in exposed)
        {
            var documented = document.ColumnsOf(obj.Schema, obj.Name);
            if (documented is null)
            {
                _logger.LogWarning("{Object} has no '## {Object}' section with a '### Columns' table in the metadata file.", obj.FullName, obj.FullName);
                continue;
            }

            var unknown = documented.Where(c => !obj.Columns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            var missing = obj.Columns.Where(c => !documented.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();

            if (unknown.Count > 0)
            {
                _logger.LogWarning("{Object}: the metadata lists columns that don't exist: {Columns}.", obj.FullName, string.Join(", ", unknown));
            }

            if (missing.Count > 0)
            {
                _logger.LogWarning("{Object}: the metadata doesn't describe these columns: {Columns}.", obj.FullName, string.Join(", ", missing));
            }
        }
    }
}
