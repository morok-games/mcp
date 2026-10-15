using System.Threading.RateLimiting;

namespace Platform.Api;

/// <summary>Rate limit policies for public endpoints.</summary>
public static class RateLimits
{
    public const string Mcp = "mcp";
    public const string Login = "login";

    /// <summary>Fixed one-minute window per client IP (the real IP when running behind nginx with forwarded headers).</summary>
    public static RateLimitPartition<string> PerIp(HttpContext context, int permitPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
}
