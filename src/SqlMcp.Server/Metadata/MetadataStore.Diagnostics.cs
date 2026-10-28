using System.Text.RegularExpressions;
using SqlMcp.Server.Catalog;

namespace SqlMcp.Server.Metadata;

// Warning when the human-written file names something the exclusion lists hide.
public sealed partial class MetadataStore
{
    private void WarnAboutHiddenNames(MetadataDocument document)
    {
        // Object entries may be "schema.object"; the bare object name is what would appear in prose.
        var objectNames = _database.ExcludeObjects.Select(e => SqlNames.Split(e)?.LastOrDefault());
        var columnNames = _database.ExcludeColumns.Values.SelectMany(columns => columns);

        foreach (var name in objectNames.Concat(columnNames).OfType<string>().Select(n => n.Trim('[', ']', '"', ' ')).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (name.Length > 0 && Regex.IsMatch(document.Markdown, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase))
            {
                _logger.LogWarning(
                    "Metadata {Path} mentions the hidden name '{Name}'. Agents reading it will learn that it exists; remove it from the file.",
                    _path, name);
            }
        }
    }
}
