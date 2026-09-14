using ChatApp.Presence;
using ChatApp.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace ChatApp.Tests;

public class RedisSharedStateTests
{
    private static Task<ConnectionMultiplexer> Connect() => ConnectionMultiplexer.ConnectAsync(
        Environment.GetEnvironmentVariable("CHATAPP_TEST_REDIS") ?? throw new InvalidOperationException("Set CHATAPP_TEST_REDIS to an isolated test Redis."));
    private static IConfiguration Configuration(string? prefix = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Redis:ChannelPrefix"] = prefix ?? "state-test-" + Guid.NewGuid().ToString("N") }).Build();
    private static IOptions<PresenceOptions> Timing() => Microsoft.Extensions.Options.Options.Create(new PresenceOptions
        { HeartbeatSeconds = 1, ConnectionTtlSeconds = 3, SweepSeconds = 1 });

    [Fact]
    public async Task Concurrent_rate_limits_are_atomic_isolated_and_expire()
    {
        using var first = await Connect();
        using var second = await Connect();
        var config = Configuration();
        var a = new RedisRateLimitCounter(first, config);
        var b = new RedisRateLimitCounter(second, config);
        var decisions = await Task.WhenAll(Enumerable.Range(0, 100).Select(i =>
            (i % 2 == 0 ? a : b).AcquireAsync("test", "same-user", 10, TimeSpan.FromSeconds(2))));
        Assert.Equal(10, decisions.Count(d => d.Allowed));
        Assert.All(decisions.Where(d => !d.Allowed), d => Assert.True(d.RetryAfter > TimeSpan.Zero));
        Assert.True((await b.AcquireAsync("test", "other-user", 10, TimeSpan.FromSeconds(2))).Allowed);
        Assert.True((await b.AcquireAsync("another-policy", "same-user", 10, TimeSpan.FromSeconds(2))).Allowed);
        Assert.True((await new RedisRateLimitCounter(second, Configuration()).AcquireAsync("test", "same-user", 10, TimeSpan.FromSeconds(2))).Allowed);
        await Task.Delay(TimeSpan.FromSeconds(2.2));
        Assert.True((await b.AcquireAsync("test", "same-user", 10, TimeSpan.FromSeconds(2))).Allowed);
    }

    [Fact]
    public async Task Multiple_connections_remain_online_until_last_lease_expires_and_old_ack_cannot_erase_change()
    {
        using var first = await Connect();
        using var second = await Connect();
        var config = Configuration();
        var a = new RedisPresenceStore(first, config, Timing());
        var b = new RedisPresenceStore(second, config, Timing());
        var user = Guid.NewGuid();
        await a.RenewAsync(user, "connection-a");
        await b.RenewAsync(user, "connection-b");
        Assert.Equal(2, await a.CountAsync(user));
        var original = Assert.Single(await a.PendingAsync(default));
        await a.RemoveAsync(user, "connection-a");
        Assert.Equal(1, await b.CountAsync(user));
        await Task.Delay(TimeSpan.FromSeconds(3.2));
        await a.SweepExpiredAsync(default);
        Assert.Equal(0, await b.CountAsync(user));
        await a.AcknowledgeAsync(user, original.Revision);
        var offline = Assert.Single(await b.PendingAsync(default));
        Assert.NotEqual(original.Revision, offline.Revision);
        await b.AcknowledgeAsync(user, offline.Revision);
        Assert.Empty(await a.PendingAsync(default));
        Assert.Equal(0, await new RedisPresenceStore(second, Configuration(), Timing()).CountAsync(user));
    }

    [Fact]
    public async Task Heartbeats_keep_connections_alive_and_stopped_api_leases_expire()
    {
        using var redis = await Connect();
        var store = new RedisPresenceStore(redis, Configuration(), Timing());
        using var tracker = new RedisPresenceTracker(store, Timing(), NullLogger<RedisPresenceTracker>.Instance);
        var user = Guid.NewGuid();
        await tracker.StartAsync(default);
        try
        {
            await tracker.AddConnectionAsync(user, "live");
            await Task.Delay(TimeSpan.FromSeconds(4));
            Assert.True(await tracker.IsOnlineAsync(user));
            await tracker.RemoveConnectionAsync(user, "live");
            await tracker.RenewLocalConnectionsAsync();
            Assert.False(await tracker.IsOnlineAsync(user));
            await tracker.AddConnectionAsync(user, "crashed");
        }
        finally { await tracker.StopAsync(default); }
        await Task.Delay(TimeSpan.FromSeconds(3.2));
        Assert.False(await tracker.IsOnlineAsync(user));
    }
}
