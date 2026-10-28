namespace SqlMcp.Server.Metadata;

/// <summary>The business-rules document of each database, if one has been written.</summary>
public interface IMetadataStore
{
    /// <summary>
    /// The current document of <paramref name="database"/> (file &lt;database&gt;.md), or null when the file
    /// doesn't exist. Re-read when the file changes.
    /// </summary>
    MetadataDocument? GetDocument(string database);
}
