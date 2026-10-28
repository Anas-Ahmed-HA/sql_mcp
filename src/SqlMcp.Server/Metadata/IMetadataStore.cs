namespace SqlMcp.Server.Metadata;

/// <summary>The business-rules document of the current connection, if one has been written.</summary>
public interface IMetadataStore
{
    /// <summary>The connection-string name; also the file name (&lt;ConnectionName&gt;.md).</summary>
    string ConnectionName { get; }

    /// <summary>The current document, or null when the file doesn't exist. Re-read when the file changes.</summary>
    MetadataDocument? GetDocument();
}
