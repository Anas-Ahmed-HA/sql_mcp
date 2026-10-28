namespace SqlMcp.Server.Catalog;

/// <summary>
/// An object name as written by a person or an agent: "PBI_PH_Assets", "dbo.PBI_PH_Assets", "[dbo].[PBI_PH_Assets]"
/// or "`dbo.PBI_PH_Assets`". The one place names are parsed and compared; matching is case-insensitive, like
/// SQL Server's default collation, and a name without a schema matches that name in any schema.
/// </summary>
public readonly partial record struct ObjectName(string? Schema, string Name)
{
    public bool IsQualified => Schema is not null;

    /// <summary>Null for anything that isn't one or two parts (empty parts, database-qualified names, unclosed brackets).</summary>
    public static ObjectName? Parse(string text) => Split(text.Replace("`", string.Empty)) switch
    {
        [var name] => new ObjectName(null, name),
        [var schema, var name] => new ObjectName(schema, name),
        _ => null,
    };

    public bool Matches(string schema, string name) =>
        string.Equals(Name, name, StringComparison.OrdinalIgnoreCase) &&
        (Schema is null || string.Equals(Schema, schema, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => Schema is null ? Name : $"{Schema}.{Name}";
}
