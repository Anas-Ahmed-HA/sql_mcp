using System.Text.Json.Serialization;

namespace SqlMcp.Server.Tools;

// Wire shapes returned to agents by the database tools.

/// <param name="Description">From the database's metadata file; omitted when none has been written.</param>
public sealed partial record DatabaseSummary(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description);
