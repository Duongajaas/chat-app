using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ChatApp.Presence;

// Connection leases use Redis time. The index survives lease-key expiry so the worker can emit offline events.
public sealed class RedisPresenceStore(IConnectionMultiplexer redis, IConfiguration configuration, IOptions<PresenceOptions> options)
{
    private readonly string prefix = configuration["Redis:ChannelPrefix"] + ":{presence}:";
    private readonly long ttl = (long)options.Value.ConnectionTtlSeconds * 1000;
    private const string UpdateScript = """
        local clock = redis.call('TIME')
        local now = clock[1] * 1000 + math.floor(clock[2] / 1000)
        local wasOnline = redis.call('ZSCORE', KEYS[2], ARGV[1]) ~= false
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', now)
        if ARGV[2] == 'upsert' then
            redis.call('ZADD', KEYS[1], now + tonumber(ARGV[4]), ARGV[3])
        elseif ARGV[2] == 'remove' then
            redis.call('ZREM', KEYS[1], ARGV[3])
        end
        local count = redis.call('ZCARD', KEYS[1])
        if count > 0 then
            local first = redis.call('ZRANGE', KEYS[1], 0, 0, 'WITHSCORES')
            redis.call('ZADD', KEYS[2], first[2], ARGV[1])
            redis.call('PEXPIRE', KEYS[1], tonumber(ARGV[4]) * 2)
        else
            redis.call('DEL', KEYS[1])
            redis.call('ZREM', KEYS[2], ARGV[1])
        end
        if wasOnline ~= (count > 0) then
            redis.call('HSET', KEYS[3], ARGV[1], ARGV[5])
        end
        return count
        """;
    private const string DueScript = """
        local clock = redis.call('TIME')
        local now = clock[1] * 1000 + math.floor(clock[2] / 1000)
        return redis.call('ZRANGEBYSCORE', KEYS[1], '-inf', now, 'LIMIT', 0, 100)
        """;
    private const string AckScript = """
        if redis.call('HGET', KEYS[1], ARGV[1]) == ARGV[2] then
            return redis.call('HDEL', KEYS[1], ARGV[1])
        end
        return 0
        """;

    public Task<int> RenewAsync(Guid userId, string connectionId) => UpdateAsync(userId, "upsert", connectionId);
    public Task<int> RemoveAsync(Guid userId, string connectionId) => UpdateAsync(userId, "remove", connectionId);
    public Task<int> CountAsync(Guid userId) => UpdateAsync(userId, "read", "");

    private async Task<int> UpdateAsync(Guid userId, string action, string connectionId)
    {
        var user = userId.ToString("N");
        return (int)(await redis.GetDatabase().ScriptEvaluateAsync(UpdateScript,
            [prefix + "user:" + user, prefix + "users", prefix + "dirty"],
            [user, action, connectionId, ttl, Guid.NewGuid().ToString("N")]));
    }

    public async Task SweepExpiredAsync(CancellationToken ct)
    {
        var due = (RedisResult[])(await redis.GetDatabase().ScriptEvaluateAsync(DueScript, [prefix + "users"])
            .WaitAsync(ct))!;
        foreach (var user in due)
        {
            ct.ThrowIfCancellationRequested();
            await CountAsync(Guid.Parse((string)user!));
        }
    }

    public async Task<IReadOnlyList<(Guid UserId, string Revision)>> PendingAsync(CancellationToken ct)
    {
        var result = new List<(Guid, string)>();
        await foreach (var entry in redis.GetDatabase().HashScanAsync(prefix + "dirty", pageSize: 100).WithCancellation(ct))
        {
            result.Add((Guid.Parse(entry.Name.ToString()), entry.Value.ToString()));
            if (result.Count == 100) break;
        }
        return result;
    }

    public Task AcknowledgeAsync(Guid userId, string revision) => redis.GetDatabase().ScriptEvaluateAsync(AckScript,
        [prefix + "dirty"], [userId.ToString("N"), revision]);
}
