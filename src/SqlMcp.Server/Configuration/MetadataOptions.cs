namespace SqlMcp.Server.Configuration;

/// <summary>Bound from the "Metadata" configuration section; where the business-rules files are.</summary>
public sealed partial class MetadataOptions
{
    public const string SectionName = "Metadata";

    /// <summary>Folder holding one &lt;ConnectionName&gt;.md per connection; relative paths are under the app folder.</summary>
    public string Directory { get; set; } = "metadata";
}
