namespace SqlMcp.Server.Security;

/// <param name="UserId">The authenticated end user. Always null until the auth phase.</param>
public sealed record CallerInfo(string? UserId, string? IpAddress, string? UserAgent);

/// <summary>
/// Who is making the current request. The single place caller identity comes from, so adding
/// authentication later changes this class and nothing in the tools.
/// </summary>
public interface ICallerContext
{
    CallerInfo Current { get; }
}

public sealed class HttpCallerContext(IHttpContextAccessor accessor) : ICallerContext
{
    public CallerInfo Current
    {
        get
        {
            var http = accessor.HttpContext;
            var userAgent = http?.Request.Headers.UserAgent.ToString();

            return new CallerInfo(
                UserId: null,
                // Behind a reverse proxy this is the proxy's address until forwarded headers are configured.
                IpAddress: http?.Connection.RemoteIpAddress?.ToString(),
                UserAgent: string.IsNullOrEmpty(userAgent) ? null : userAgent);
        }
    }
}
