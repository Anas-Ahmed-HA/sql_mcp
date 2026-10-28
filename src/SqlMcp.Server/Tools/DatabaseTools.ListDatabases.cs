using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SqlMcp.Server.Tools;

// The list_databases tool.
public sealed partial class DatabaseTools
{
    [McpServerTool(Name = "list_databases", Title = "List databases", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the databases available on this server, with what each one is for. Start here: every other tool " +
        "takes one of these names as 'database'. Each database is separate; a query can only read one.")]
    public IReadOnlyList<DatabaseSummary> ListDatabases() =>
        _access.Available
            .Select(d => new DatabaseSummary(d, _metadata.GetDocument(d)?.Summary))
            .ToList();
}
