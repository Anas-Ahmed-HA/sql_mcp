using System.Text.Json.Serialization;
using SqlMcp.Server.Query;

namespace SqlMcp.Server.Tools;

// Wire shapes returned to agents by the query tools.

public sealed partial record RunSelectResult(
    IReadOnlyList<QueryColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    int RowCount,
    bool Truncated,
    long ElapsedMs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note);
