namespace SqlMcp.Server.Catalog;

// The parsed form of an exclusion entry's object name.
public sealed partial class ExclusionPolicy
{
    private readonly partial record struct ObjectKey(string? Schema, string Name)
    {
        public static ObjectKey? TryParse(string entry) => SqlNames.Split(entry) switch
        {
            [var name] => new ObjectKey(null, name),
            [var schema, var name] => new ObjectKey(schema, name),
            _ => null,
        };

        public bool Matches(string schema, string name) =>
            Comparer.Equals(Name, name) && (Schema is null || Comparer.Equals(Schema, schema));

        public override string ToString() => Schema is null ? Name : $"{Schema}.{Name}";
    }
}
