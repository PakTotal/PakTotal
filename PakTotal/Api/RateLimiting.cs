using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PakTotal.Core;

namespace PakTotal.Api;

public static class RateLimiting
{
    public const string SubmitPolicy = "scan-submit";
    public const string ReadPolicy = "api-read";

    public static IServiceCollection AddPakTotalRateLimiting(this IServiceCollection services, PakTotalOptions options)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(new ApiErrorResponse("Too many requests. Try again later."), token);
            };

            limiter.AddPolicy(SubmitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => Window(options.SubmitPermitsPerMinute)));
            limiter.AddPolicy(ReadPolicy, context => IsLoopback(context)
                ? RateLimitPartition.GetNoLimiter("loopback")
                : RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => Window(options.ReadPermitsPerMinute)));
        });
        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    };

    private static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static bool IsLoopback(HttpContext context) => context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
}
