using System.Text.Json.Serialization;

namespace SqlMcp.Server.Catalog;

public enum DbObjectKind
{
    Table,
    View,
}

/// <summary>An exposed (whitelisted) table or view. ObjectId is internal and never returned to agents.</summary>
public sealed partial record CatalogObject(
    int ObjectId,
    string Schema,
    string Name,
    DbObjectKind Kind,
    string? Description,
    IReadOnlyList<string> Columns,
    long? ApproxRowCount)
{
    public string FullName => $"{Schema}.{Name}";

    public int ColumnCount => Columns.Count;
}

public sealed partial record ObjectSchema(
    IReadOnlyList<ColumnInfo> Columns,
    KeyInfo? PrimaryKey,
    IReadOnlyList<KeyInfo> UniqueKeys,
    IReadOnlyList<CheckConstraintInfo> CheckConstraints,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys,
    IReadOnlyList<DependencyInfo> DependsOn);

// Returned to agents as is by describe_object; optional fields are omitted when empty.
public sealed partial record ColumnInfo(
    string Name,
    string Type,
    bool Nullable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Identity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Computed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ComputedDefinition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description);

public sealed partial record KeyInfo(string Name, IReadOnlyList<string> Columns);

public sealed partial record CheckConstraintInfo(string Name, string? Column, string? Definition, bool Disabled);

/// <summary>A foreign key in either direction. Only returned when both ends are exposed objects.</summary>
public sealed partial record ForeignKeyInfo(
    string Name,
    string FromObject,
    IReadOnlyList<string> FromColumns,
    string ToObject,
    IReadOnlyList<string> ToColumns,
    string OnDelete,
    string OnUpdate,
    bool Disabled);

/// <summary>An exposed object that a view (or computed column) reads from.</summary>
public sealed partial record DependencyInfo(string Name, string Type);
