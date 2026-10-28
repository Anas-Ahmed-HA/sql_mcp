using System.Text.Json.Serialization;
using SqlMcp.Server.Catalog;

namespace SqlMcp.Server.Tools;

// Wire shapes returned to agents by the catalog tools. Optional fields are omitted when empty to keep responses small.

public sealed partial record ListObjectsResult(int Total, bool Truncated, IReadOnlyList<ObjectSummary> Objects);

public sealed partial record ObjectSummary(
    string Name,
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
    int Columns,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ApproxRows);

public sealed partial record DescribeObjectResult(
    string Name,
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ApproxRows,
    IReadOnlyList<ColumnInfo> Columns,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] KeyInfo? PrimaryKey,
    IReadOnlyList<KeyInfo> UniqueKeys,
    IReadOnlyList<CheckConstraintInfo> CheckConstraints,
    IReadOnlyList<OutgoingForeignKey> References,
    IReadOnlyList<IncomingForeignKey> ReferencedBy,
    IReadOnlyList<DependencyInfo> DependsOn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BusinessMetadata,
    IReadOnlyList<string> Notes);

/// <summary>This object's columns point at another object's key.</summary>
public sealed partial record OutgoingForeignKey(
    string Name,
    IReadOnlyList<string> Columns,
    string ReferencesObject,
    IReadOnlyList<string> ReferencesColumns,
    string OnDelete,
    string OnUpdate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Disabled);

/// <summary>Another object's columns point at this object's key.</summary>
public sealed partial record IncomingForeignKey(
    string Name,
    string FromObject,
    IReadOnlyList<string> FromColumns,
    IReadOnlyList<string> Columns,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Disabled);
