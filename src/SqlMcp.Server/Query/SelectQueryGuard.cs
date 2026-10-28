using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlMcp.Server.Catalog;
using SqlMcp.Server.Errors;

namespace SqlMcp.Server.Query;

/// <summary>
/// Decides whether agent-written SQL may run. Works on the parsed syntax tree, never on text patterns,
/// so comments, casing, brackets and whitespace tricks don't matter.
///
/// Allowed: exactly one SELECT statement that reads only exposed (whitelisted) objects from the catalog
/// snapshot. The database login is still the last line of defense.
/// </summary>
public sealed partial class SelectQueryGuard
{
    public const int MaxSqlLength = 20_000;

    public GuardResult Validate(string sql, CatalogSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return GuardResult.Reject(AgentErrors.NotASingleSelect("The query is empty."));
        }

        if (sql.Length > MaxSqlLength)
        {
            return GuardResult.Reject(AgentErrors.QueryTooLong(MaxSqlLength));
        }

        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out var errors);

        if (errors.Count > 0)
        {
            var e = errors[0];
            return GuardResult.Reject(AgentErrors.SyntaxError(e.Line, e.Column, e.Message));
        }

        if (fragment is not TSqlScript { Batches: [{ Statements: [var statement] }] })
        {
            return GuardResult.Reject(AgentErrors.NotASingleSelect("Only a single SELECT statement is allowed."));
        }

        if (statement is not SelectStatement select)
        {
            return GuardResult.Reject(AgentErrors.NotASingleSelect(
                $"Only SELECT statements are allowed (got {statement.GetType().Name.Replace("Statement", string.Empty)})."));
        }

        if (select.Into is not null)
        {
            return GuardResult.Reject(AgentErrors.NotASingleSelect("SELECT ... INTO is not allowed."));
        }

        var cteNames = select.WithCtesAndXmlNamespaces?.CommonTableExpressions
            .Select(c => c.ExpressionName.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var visitor = new Visitor(snapshot, cteNames);
        select.Accept(visitor);

        return visitor.Error is null
            ? GuardResult.Allow(visitor.Objects.Distinct().ToList())
            : GuardResult.Reject(visitor.Error);
    }
}
