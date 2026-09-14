using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ChatApp.Presence;

public sealed class RedisPresenceTracker(RedisPresenceStore store, IOptions<PresenceOptions> options,
    ILogger<RedisPresenceTracker> logger) : BackgroundService, IPresenceTracker
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, Guid> connections = new();

    public async Task AddConnectionAsync(Guid userId, string connectionId)
    {
        await gate.WaitAsync();
        try
        {
            await store.RenewAsync(userId, connectionId);
            connections[connectionId] = userId;
        }
        finally { gate.Release(); }
    }

    public async Task RemoveConnectionAsync(Guid userId, string connectionId)
    {
        await gate.WaitAsync();
        try
        {
            // Remove locally even during an outage: a later heartbeat must not resurrect this connection.
            connections.Remove(connectionId);
            await store.RemoveAsync(userId, connectionId);
        }
        finally { gate.Release(); }
    }

    public async Task<bool> IsOnlineAsync(Guid userId) => await store.CountAsync(userId) > 0;
    public Task<int> GetConnectionCountAsync(Guid userId) => store.CountAsync(userId);

    public async Task RenewLocalConnectionsAsync(CancellationToken ct = default)
    {
        // Serialize renew/remove to prevent a heartbeat captured before disconnect from reviving a lease.
        await gate.WaitAsync(ct);
        try
        {
            foreach (var batch in connections.Chunk(100))
            {
                ct.ThrowIfCancellationRequested();
                await Task.WhenAll(batch.Select(c => store.RenewAsync(c.Value, c.Key)));
            }
        }
        finally { gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.HeartbeatSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RenewLocalConnectionsAsync(stoppingToken); }
            catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
            { logger.LogWarning("Presence heartbeat unavailable; leases will expire until Redis recovers"); }
        }
    }
}
