using Microsoft.AspNetCore.SignalR;
using ChatApp.RateLimiting;
using StackExchange.Redis;

namespace ChatApp.Realtime;

public sealed class HubRateLimitFilter(IRateLimitCounter counter) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        // CallService applies separate distributed control/SDP/ICE quotas.
        if (context.HubMethodName is "InitiateCall" or "UpdateCall" or "SendSignal") return await next(context);
        RateLimitDecision decision;
        try
        {
            decision = await counter.AcquireAsync("hub", context.Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Context.ConnectionId,
                60, TimeSpan.FromSeconds(10), context.Context.ConnectionAborted);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            throw new HubException("Dịch vụ tạm thời gián đoạn. Vui lòng thử lại.");
        }
        if (!decision.Allowed) throw new HubException("Bạn thao tác quá nhanh. Vui lòng thử lại.");
        return await next(context);
    }
}

