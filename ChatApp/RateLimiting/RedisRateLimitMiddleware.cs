using System.Globalization;
using System.Security.Claims;
using ChatApp.Common;
using ChatApp.Options;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ChatApp.RateLimiting;

public sealed class RedisRateLimitMiddleware(RequestDelegate next, ILogger<RedisRateLimitMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IRateLimitCounter counter, IOptions<RateLimitingOptions> options)
    {
        var endpoint = context.GetEndpoint();
        var policy = endpoint?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        if (policy == null || endpoint!.Metadata.GetMetadata<DisableRateLimitingAttribute>() != null)
        {
            await next(context);
            return;
        }
        var configured = options.Value;
        var rule = policy switch
        {
            "group-write" => new RateLimitRule { PermitLimit = 30, WindowSeconds = 60 },
            "group-join" => new RateLimitRule { PermitLimit = 10, WindowSeconds = 60 },
            "chat-write" => new RateLimitRule { PermitLimit = 60, WindowSeconds = 60 },
            RateLimitPolicies.Login => configured.Login,
            RateLimitPolicies.Register => configured.Register,
            RateLimitPolicies.RefreshToken => configured.RefreshToken,
            RateLimitPolicies.ForgotPassword => configured.ForgotPassword,
            RateLimitPolicies.ResetPassword => configured.ResetPassword,
            RateLimitPolicies.GoogleLogin => configured.GoogleLogin,
            RateLimitPolicies.ResolveFriendLink => configured.ResolveFriendLink,
            RateLimitPolicies.GetMessages => configured.GetMessages,
            _ => throw new InvalidOperationException("Unknown rate limit policy: " + policy)
        };
        var ip = context.Connection.RemoteIpAddress;
        var partition = "ip:" + (ip?.IsIPv4MappedToIPv6 == true ? ip.MapToIPv4().ToString() : ip?.ToString() ?? "unknown");
        if ((policy is "chat-write" or "group-write" or "group-join") && context.User.FindFirstValue(ClaimTypes.NameIdentifier) is string user)
            partition = "user:" + user;

        RateLimitDecision decision;
        try
        {
            decision = await counter.AcquireAsync(policy, partition, rule.PermitLimit,
                TimeSpan.FromSeconds(rule.WindowSeconds), context.RequestAborted);
            if (decision.Allowed && policy == "group-join")
                decision = await counter.AcquireAsync("group-join-ip", "ip:" + (ip?.MapToIPv6().ToString() ?? "unknown"),
                    30, TimeSpan.FromMinutes(1), context.RequestAborted);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning("Redis rate limiting unavailable");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "5";
            await context.Response.WriteAsJsonAsync(new { message = "Dịch vụ tạm thời gián đoạn. Vui lòng thử lại." }, context.RequestAborted);
            return;
        }
        if (!decision.Allowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(new { message = "Bạn thao tác quá nhiều lần, vui lòng thử lại sau." }, context.RequestAborted);
            return;
        }
        await next(context);
    }
}
