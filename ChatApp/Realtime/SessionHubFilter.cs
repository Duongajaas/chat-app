using System.Security.Claims;
using ChatApp.Data;
using ChatApp.Services;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Realtime;

// SignalR's user target is a login session, never all connections of an account.
public sealed class SessionUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        Guid.TryParse(connection.User.FindFirstValue("sid"), out var id) ? id.ToString() : null;
}

public sealed class SessionHubFilter(IServiceScopeFactory scopes, SessionConnectionMonitor monitor) : IHubFilter
{
    private async Task Validate(HubCallerContext context)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await SessionAccess.IsActiveAsync(db, context.User, context.ConnectionAborted)) return;
        context.Abort();
        throw new HubException("Phiên đăng nhập đã hết hiệu lực.");
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        await Validate(context.Context);
        return await next(context);
    }

    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        monitor.Remove(context.Context.ConnectionId);
        await next(context, exception);
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        await Validate(context.Context);
        monitor.Add(context.Context);
        try { await next(context); }
        catch { monitor.Remove(context.Context.ConnectionId); throw; }
    }
}
