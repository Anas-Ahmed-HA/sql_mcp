using Microsoft.Data.SqlClient;

namespace SqlMcp.Server.Errors;

// SQL Server errors while running a guarded query, mapped by error number to a code and a fix.
// The message is SQL Server's own: it names the column or value, which is what the agent needs to fix the query.
public static partial class AgentErrors
{
    public static AgentError FromSqlException(SqlException ex) => ex.Number switch
    {
        207 => new("invalid_column", ex.Message,
            "Call describe_object for the exact column names of the object and use one of them."),
        208 => UnknownObject(ObjectNameFrom(ex.Message)),
        209 => new("ambiguous_column", ex.Message,
            "Prefix the column with its table alias, e.g. a.Company."),
        8120 => new("not_in_group_by", ex.Message,
            "Add the column to GROUP BY, or wrap it in an aggregate such as COUNT, MAX or MIN."),
        8134 => new("divide_by_zero", ex.Message,
            "Guard the divisor: x / NULLIF(y, 0)."),
        241 or 242 or 245 or 248 or 8114 or 8115 => new("conversion_failed", ex.Message,
            "Check the column's type with describe_object. Compare with a value of that type, or use TRY_CAST/TRY_CONVERT " +
            "to skip values that don't convert."),
        1205 => new("deadlock", ex.Message,
            "Run the same query again.", Retryable: true),
        _ => new("sql_error", ex.Message,
            "Fix the query using SQL Server's message."),
    };

    // "Invalid object name 'dbo.X'." -> "dbo.X", so 208 from SQL Server reads exactly like the guard's own error.
    private static string ObjectNameFrom(string message) =>
        message.IndexOf('\'') is var start and >= 0 && message.LastIndexOf('\'') is var end && end > start
            ? message[(start + 1)..end]
            : message;
}
