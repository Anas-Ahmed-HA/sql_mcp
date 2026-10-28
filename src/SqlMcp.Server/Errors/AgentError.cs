using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace SqlMcp.Server.Errors;

/// <summary>
/// An error an agent can act on. Returned to the agent as JSON (see Program.cs) instead of a bare message.
/// </summary>
/// <param name="Code">Stable, machine-readable kind of error (see <see cref="AgentErrors"/>); agents can branch on it.</param>
/// <param name="Message">What went wrong, in SQL Server's wording where it comes from SQL Server.</param>
/// <param name="Fix">What the agent should do next.</param>
/// <param name="Retryable">True when the same call may succeed later unchanged (outage, deadlock).</param>
public sealed partial record AgentError(string Code, string Message, string Fix, bool Retryable = false)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep quotes and Arabic text readable for the agent
    };

    public AgentErrorException ToException(Exception? inner = null) => new(this, inner);

    /// <summary>A failed tool result whose text is this error as JSON: {"code","message","fix","retryable"}.</summary>
    public CallToolResult ToToolResult() => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(this, JsonOptions) }],
    };
}

/// <summary>Thrown by tools and the services they call for expected, agent-facing errors.</summary>
public sealed partial class AgentErrorException(AgentError error, Exception? inner = null) : McpException(error.Message, inner)
{
    public AgentError Error { get; } = error;
}
