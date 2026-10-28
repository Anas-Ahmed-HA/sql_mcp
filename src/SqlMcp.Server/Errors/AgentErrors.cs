namespace SqlMcp.Server.Errors;

/// <summary>
/// Every error agents can get, in one place, so codes and fixes stay consistent across tools.
/// A not-allowed database or object gets exactly the same error as a missing one.
/// </summary>
public static partial class AgentErrors
{
    // Request errors.

    public static AgentError MissingArgument(string message) => new(
        "missing_argument", message,
        "Every tool except list_databases needs 'database': call list_databases for the names. " +
        "If your tool list doesn't show that argument, reconnect to refresh it.");

    public static AgentError InvalidArgument(string message, string fix) => new("invalid_argument", message, fix);

    // Databases.

    public static AgentError UnknownDatabase(string database) => new(
        "unknown_database", $"Database '{database}' was not found.",
        "Call list_databases and pass one of the names it returns as 'database'.");

    public static AgentError DatabaseUnavailable(string database) => new(
        "database_unavailable", $"Database '{database}' can't be reached right now.",
        "Try again in a few minutes, or use another database.", Retryable: true);

    // Objects.

    public static AgentError UnknownObject(string name) => new(
        "unknown_object", $"Invalid object name '{name}'.",
        "Use only objects that list_objects returns for this database, written as schema.object.");

    public static AgentError AmbiguousObject(string name, IEnumerable<string> candidates) => new(
        "ambiguous_object", $"'{name}' is ambiguous: {string.Join(", ", candidates)}.",
        "Write the name with its schema, as schema.object.");

    // Queries rejected by the guard, before anything runs.

    public static AgentError SyntaxError(int line, int column, string message) => new(
        "syntax_error", $"Syntax error at line {line}, column {column}: {message}",
        "Fix the T-SQL at that position. This is SQL Server: use TOP n instead of LIMIT, and [brackets] or " +
        "\"double quotes\" for names with spaces.");

    public static AgentError NotASingleSelect(string message) => new(
        "not_a_single_select", message,
        "Send exactly one SELECT statement (WITH ... CTEs are fine) with no INTO, and nothing before or after it.");

    public static AgentError QueryTooLong(int maxLength) => new(
        "query_too_long", $"The query is longer than {maxLength} characters.",
        "Shorten it: select only the columns you need and aggregate instead of listing values.");

    public static AgentError CrossDatabase(string name) => new(
        "cross_database", $"'{name}': database- or server-qualified names are not allowed.",
        "A query reads only the database passed as 'database'. Write schema.object, and query each database " +
        "with its own run_select call.");

    public static AgentError BlockedConstruct(string message) => new(
        "blocked_construct", message,
        "Rewrite the query with only SELECT over objects from list_objects, joins, subqueries, CTEs, built-in " +
        "scalar and aggregate functions.");

    // Query execution.

    public static AgentError Timeout(int seconds) => new(
        "timeout", $"The query timed out after {seconds} s.",
        "Make it cheaper: filter with WHERE, aggregate with COUNT/GROUP BY, or lower TOP. Running it unchanged will time out again.");
}
