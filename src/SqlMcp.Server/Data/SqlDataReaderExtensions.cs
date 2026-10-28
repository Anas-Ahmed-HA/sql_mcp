using Microsoft.Data.SqlClient;

namespace SqlMcp.Server.Data;

internal static class SqlDataReaderExtensions
{
    public static string? GetStringOrNull(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static int? GetInt32OrNull(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    public static long? GetInt64OrNull(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
}
