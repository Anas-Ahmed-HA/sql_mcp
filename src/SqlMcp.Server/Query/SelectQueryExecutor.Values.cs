namespace SqlMcp.Server.Query;

// Converting SQL Server values into compact, JSON-friendly ones.
public sealed partial class SelectQueryExecutor
{
    private static object? ToJsonValue(object value, int maxCellLength) => value switch
    {
        DBNull => null,
        string s => s.Length > maxCellLength ? string.Concat(s.AsSpan(0, maxCellLength), "…") : s,
        bool or byte or short or int or long or float or double or decimal => value,
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd") : d.ToString("yyyy-MM-ddTHH:mm:ss.FFF"),
        DateTimeOffset o => o.ToString("O"),
        TimeSpan t => t.ToString(),
        Guid g => g.ToString(),
        byte[] b => $"<binary, {b.Length} bytes>",
        _ => value.ToString(),
    };
}
