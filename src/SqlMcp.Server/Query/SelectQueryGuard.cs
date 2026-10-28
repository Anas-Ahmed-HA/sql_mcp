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
public sealed partial class SelectQueryGuard
{
    public const int MaxSqlLength = 20_000;

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
}
