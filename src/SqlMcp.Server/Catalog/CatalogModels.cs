using SqlMcp.Server.Configuration;

namespace SqlMcp.Server.Catalog;

public enum DbObjectKind
{
    Table,
    View,
}

/// <summary>A table or view the server exposes. ObjectId is internal and never returned to agents.</summary>
/// <param name="Columns">Visible columns, in column order. Hidden columns are not included.</param>
/// <param name="HiddenColumns">Columns removed by ExcludeColumns. Never returned to agents; used to filter and to guard queries.</param>
public sealed record CatalogObject(
    int ObjectId,
    string Schema,
    string Name,
    DbObjectKind Kind,
    string? Description,
    IReadOnlyList<string> Columns,
    IReadOnlySet<string> HiddenColumns,
    long? ApproxRowCount)
{
    public string FullName => $"{Schema}.{Name}";

    public int ColumnCount => Columns.Count;
}

public sealed record ObjectSchema(
    IReadOnlyList<ColumnInfo> Columns,
    KeyInfo? PrimaryKey,
    IReadOnlyList<KeyInfo> UniqueKeys,
    IReadOnlyList<CheckConstraintInfo> CheckConstraints,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys,
    IReadOnlyList<DependencyInfo> DependsOn);

public sealed record ColumnInfo(
    string Name,
    string Type,
    bool Nullable,
    bool Identity,
    bool Computed,
    string? Default,
    string? ComputedDefinition,
    string? Description);

public sealed record KeyInfo(string Name, IReadOnlyList<string> Columns);

public sealed record CheckConstraintInfo(string Name, string? Column, string? Definition, bool Disabled);

/// <summary>A foreign key in either direction. Only returned when both ends are inside the object scope.</summary>
public sealed record ForeignKeyInfo(
    string Name,
    string FromObject,
    IReadOnlyList<string> FromColumns,
    string ToObject,
    IReadOnlyList<string> ToColumns,
    string OnDelete,
    string OnUpdate,
    bool Disabled);

/// <summary>An object a view (or computed column) reads from. InScope says whether agents may query it.</summary>
public sealed record DependencyInfo(string Name, string Type, bool InScope);

public static class ObjectScopeExtensions
{
    public static bool Includes(this ObjectScope scope, DbObjectKind kind) => scope switch
    {
        ObjectScope.Views => kind == DbObjectKind.View,
        ObjectScope.Tables => kind == DbObjectKind.Table,
        _ => true,
    };
}
