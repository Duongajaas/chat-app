using Microsoft.AspNetCore.SignalR;
using ChatApp.RateLimiting;
using StackExchange.Redis;

namespace ChatApp.Realtime;

public sealed class HubRateLimitFilter(IRateLimitCounter counter) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        RateLimitDecision decision;
        try
        {
            decision = await counter.AcquireAsync("hub", context.Context.UserIdentifier ?? context.Context.ConnectionId,
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
