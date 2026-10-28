using Microsoft.Extensions.Options;
using SqlMcp.Server.Configuration;
using SqlMcp.Server.Errors;

namespace SqlMcp.Server.Security;

/// <summary>
/// Which databases the current caller may use. Every tool resolves its <c>database</c> argument here before
/// touching anything, so this is the one place a per-agent rule (API keys, later user identity) plugs in.
/// </summary>
public interface IDatabaseAccess
{
    /// <summary>The databases the current caller may use, in configuration order.</summary>
    IReadOnlyList<string> Available { get; }

    /// <summary>
    /// The configured name for <paramref name="database"/> (case-insensitive). Throws an agent-facing error
    /// when it doesn't exist or the caller may not use it; both get the same wording.
    /// </summary>
    string Resolve(string database);
}

/// <summary>For now every caller may use every configured database.</summary>
public sealed partial class AllDatabasesAccess(IOptions<DatabaseOptions> options) : IDatabaseAccess
{
    public IReadOnlyList<string> Available { get; } = options.Value.ConnectionStrings.Keys.ToList();

    public string Resolve(string database) =>
        Available.FirstOrDefault(d => string.Equals(d, database.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw AgentErrors.UnknownDatabase(database).ToException();
}
