using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlMcp.Server.Query;

// The allow and block lists the visitor checks against.
public sealed partial class SelectQueryGuard
{
    // Table sources that only read from other table sources or from literal input.
    private static readonly HashSet<Type> AllowedTableReferences =
    [
        typeof(NamedTableReference),
        typeof(QueryDerivedTable),
        typeof(QualifiedJoin),
        typeof(UnqualifiedJoin),
        typeof(JoinParenthesisTableReference),
        typeof(InlineDerivedTable),
        typeof(PivotedTableReference),
        typeof(UnpivotedTableReference),
        typeof(OpenJsonTableReference),
        typeof(GlobalFunctionTableReference), // STRING_SPLIT, GENERATE_SERIES
    ];

    // Built-in metadata functions that could confirm hidden objects or columns exist, or read definitions.
    private static readonly HashSet<string> BlockedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "OBJECT_ID", "OBJECT_NAME", "OBJECT_SCHEMA_NAME", "OBJECT_DEFINITION", "OBJECTPROPERTY", "OBJECTPROPERTYEX",
        "COL_NAME", "COL_LENGTH", "COLUMNPROPERTY", "INDEXPROPERTY", "INDEX_COL", "INDEXKEY_PROPERTY",
        "HAS_PERMS_BY_NAME", "TYPE_ID", "TYPEPROPERTY", "SCHEMA_ID", "DATABASEPROPERTYEX", "FILEPROPERTY",
    };
}
