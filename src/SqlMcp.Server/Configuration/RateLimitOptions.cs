namespace SqlMcp.Server.Configuration;

/// <summary>Bound from the "RateLimit" configuration section; per-IP limit on the MCP endpoint.</summary>
public sealed partial class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>
    /// MCP requests one IP address may make per minute; more get HTTP 429. Every agent step is a request
    /// (initialize, tools/list, each tool call), so keep this well above what one agent needs per minute.
    /// </summary>
    public int RequestsPerMinute { get; set; } = 120;
}
