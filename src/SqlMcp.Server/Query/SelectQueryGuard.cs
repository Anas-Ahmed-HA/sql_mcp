using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlMcp.Server.Catalog;

namespace SqlMcp.Server.Query;

/// <summary>
/// Decides whether agent-written SQL may run. Works on the parsed syntax tree, never on text patterns,
/// so comments, casing, brackets and whitespace tricks don't matter.
///
/// Allowed: exactly one SELECT statement that reads only visible objects from the catalog snapshot and
/// references no hidden column. The database login is still the last line of defense.
/// </summary>
public sealed class SelectQueryGuard
{
    public const int MaxSqlLength = 20_000;

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

    public GuardResult Validate(string sql, CatalogSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return GuardResult.Reject("The query is empty.");
        }

        if (sql.Length > MaxSqlLength)
        {
            return GuardResult.Reject($"The query is longer than {MaxSqlLength} characters.");
        }

        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out var errors);

        if (errors.Count > 0)
        {
            var e = errors[0];
            return GuardResult.Reject($"Syntax error at line {e.Line}, column {e.Column}: {e.Message}");
        }

        if (fragment is not TSqlScript { Batches: [{ Statements: [var statement] }] })
        {
            return GuardResult.Reject("Only a single SELECT statement is allowed.");
        }

        if (statement is not SelectStatement select)
        {
            return GuardResult.Reject($"Only SELECT statements are allowed (got {Describe(statement)}).");
        }

        if (select.Into is not null)
        {
            return GuardResult.Reject("SELECT ... INTO is not allowed.");
        }

        var cteNames = select.WithCtesAndXmlNamespaces?.CommonTableExpressions
            .Select(c => c.ExpressionName.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var visitor = new Visitor(snapshot, cteNames);
        select.Accept(visitor);

        if (visitor.Error is not null)
        {
            return GuardResult.Reject(visitor.Error);
        }

        // Column checks run after the walk, once every referenced object is known.
        var objects = visitor.Objects.Distinct().ToList();
        var hidden = objects.SelectMany(o => o.HiddenColumns).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (hidden.Count > 0)
        {
            if (visitor.HasSelectStar)
            {
                var withHidden = objects.Where(o => o.HiddenColumns.Count > 0).Select(o => o.FullName);
                return GuardResult.Reject(
                    $"SELECT * is not allowed on {string.Join(", ", withHidden)}. List the columns explicitly (see describe_object).");
            }

            // Same wording as SQL Server's own error, so a hidden column is indistinguishable from a missing one.
            if (visitor.ColumnNames.FirstOrDefault(hidden.Contains) is { } column)
            {
                return GuardResult.Reject($"Invalid column name '{column}'.");
            }
        }

        return GuardResult.Allow(objects);
    }

    private static string Describe(TSqlStatement statement) =>
        statement.GetType().Name.Replace("Statement", string.Empty);

    private sealed class Visitor(CatalogSnapshot snapshot, HashSet<string> cteNames) : TSqlFragmentVisitor
    {
        public string? Error { get; private set; }

        public List<CatalogObject> Objects { get; } = [];

        public List<string> ColumnNames { get; } = [];

        public bool HasSelectStar { get; private set; }

        private void Fail(string message) => Error ??= message;

        public override void Visit(TableReference node)
        {
            if (!AllowedTableReferences.Contains(node.GetType()))
            {
                Fail($"{node.GetType().Name.Replace("TableReference", string.Empty)} is not allowed as a table source. " +
                     "Only tables and views from list_objects can be queried.");
            }
        }

        public override void Visit(NamedTableReference node)
        {
            var name = node.SchemaObject;
            var display = string.Join('.', name.Identifiers.Select(i => i.Value));

            if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            {
                Fail($"'{display}': database- or server-qualified names are not allowed. Use schema.object.");
                return;
            }

            var schema = name.SchemaIdentifier?.Value;
            var objectName = name.BaseIdentifier.Value;
            if (schema is null && cteNames.Contains(objectName))
            {
                return;
            }

            var resolution = snapshot.Resolve(schema, objectName);
            if (resolution.Match is { } obj)
            {
                Objects.Add(obj);
            }
            else if (resolution.Candidates.Count > 1)
            {
                Fail($"'{display}' is ambiguous. Specify the schema: {string.Join(", ", resolution.Candidates.Select(c => c.FullName))}.");
            }
            else
            {
                // Same wording as SQL Server, so a hidden object is indistinguishable from a missing one.
                Fail($"Invalid object name '{display}'.");
            }
        }

        public override void Visit(ColumnReferenceExpression node)
        {
            switch (node.ColumnType)
            {
                case ColumnType.Wildcard: // COUNT(*)
                    break;
                case ColumnType.Regular when node.MultiPartIdentifier is { Count: > 0 } id:
                    ColumnNames.Add(id.Identifiers[^1].Value);
                    break;
                default:
                    Fail($"Column reference type {node.ColumnType} is not allowed.");
                    break;
            }
        }

        public override void Visit(SelectStarExpression node) => HasSelectStar = true;

        public override void Visit(FunctionCall node)
        {
            if (node.CallTarget is not null)
            {
                Fail($"Calling '{node.FunctionName.Value}' on a schema or object is not allowed (user-defined functions and methods are blocked).");
            }
            else if (BlockedFunctions.Contains(node.FunctionName.Value))
            {
                Fail($"The metadata function {node.FunctionName.Value.ToUpperInvariant()} is not allowed.");
            }
        }

        public override void Visit(SelectSetVariable node) => Fail("Assigning to variables is not allowed.");

        public override void Visit(NextValueForExpression node) => Fail("NEXT VALUE FOR is not allowed (it changes a sequence).");
    }
}

public sealed record GuardResult(bool IsAllowed, string? Error, IReadOnlyList<CatalogObject> Objects)
{
    public static GuardResult Allow(IReadOnlyList<CatalogObject> objects) => new(true, null, objects);

    public static GuardResult Reject(string error) => new(false, error, []);
}
