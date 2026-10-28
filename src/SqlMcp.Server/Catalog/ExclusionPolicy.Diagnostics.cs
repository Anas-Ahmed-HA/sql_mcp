namespace SqlMcp.Server.Catalog;

// Detecting exclusion entries that hide nothing (typos), so they can be logged.
public sealed partial class ExclusionPolicy
{
    /// <summary>
    /// Entries that are malformed or match nothing in the database. Each is likely a typo, which would
    /// leave the object or column visible, so the caller should log them loudly.
    /// </summary>
    public IReadOnlyList<string> FindIneffectiveEntries(IReadOnlyCollection<(string Schema, string Name, IReadOnlyList<string> Columns)> allObjects)
    {
        var problems = new List<string>(_invalidEntries);

        problems.AddRange(_objects
            .Where(k => !allObjects.Any(o => k.Matches(o.Schema, o.Name)))
            .Select(k => $"ExcludeObjects: '{k}' matches no table or view"));

        foreach (var (key, column) in _columns)
        {
            var targets = allObjects.Where(o => key is null || key.Value.Matches(o.Schema, o.Name)).ToList();
            if (key is not null && targets.Count == 0)
            {
                problems.Add($"ExcludeColumns: '{key}' matches no table or view");
            }
            else if (!targets.Any(o => o.Columns.Contains(column, Comparer)))
            {
                problems.Add($"ExcludeColumns: column '{column}' not found in '{(key?.ToString() ?? AllObjects)}'");
            }
        }

        return problems.Distinct().ToList();
    }
}
