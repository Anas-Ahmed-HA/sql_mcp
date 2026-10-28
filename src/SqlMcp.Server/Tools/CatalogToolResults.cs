using System.Text.Json.Serialization;
using SqlMcp.Server.Catalog;

namespace SqlMcp.Server.Tools;

// Wire shapes returned to agents. Optional fields are omitted when empty to keep responses small.

public sealed record ListObjectsResult(
    string Scope,
    int Total,
    bool Truncated,
    IReadOnlyList<ObjectSummary> Objects);

public sealed record ObjectSummary(
    string Name,
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
    int Columns,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ApproxRows);

public sealed record DescribeObjectResult(
    string Name,
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ApproxRows,
    IReadOnlyList<ColumnResult> Columns,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] KeyInfo? PrimaryKey,
    IReadOnlyList<KeyInfo> UniqueKeys,
    IReadOnlyList<CheckConstraintInfo> CheckConstraints,
    IReadOnlyList<OutgoingForeignKey> References,
    IReadOnlyList<IncomingForeignKey> ReferencedBy,
    IReadOnlyList<DependencyInfo> DependsOn,
    IReadOnlyList<string> Notes);

public sealed record ColumnResult(
    string Name,
    string Type,
    bool Nullable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Identity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Computed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ComputedDefinition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description);

/// <summary>This object's columns point at another object's key.</summary>
public sealed record OutgoingForeignKey(
    string Name,
    IReadOnlyList<string> Columns,
    string ReferencesObject,
    IReadOnlyList<string> ReferencesColumns,
    string OnDelete,
    string OnUpdate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Disabled);

/// <summary>Another object's columns point at this object's key.</summary>
public sealed record IncomingForeignKey(
    string Name,
    string FromObject,
    IReadOnlyList<string> FromColumns,
    IReadOnlyList<string> Columns,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Disabled);
