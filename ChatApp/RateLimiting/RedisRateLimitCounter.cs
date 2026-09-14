using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace ChatApp.RateLimiting;

public sealed class RedisRateLimitCounter(IConnectionMultiplexer redis, IConfiguration configuration) : IRateLimitCounter
{
    private readonly string prefix = configuration["Redis:ChannelPrefix"] + ":rate:";
    private const string AcquireScript = """
        local count = tonumber(redis.call('GET', KEYS[1]) or '0')
        if count >= tonumber(ARGV[1]) then
            return {0, math.max(1, redis.call('PTTL', KEYS[1]))}
        end
        count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[2]) end
        return {1, math.max(1, redis.call('PTTL', KEYS[1]))}
        """;

    public async Task<RateLimitDecision> AcquireAsync(string policy, string partition, int limit, TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0 || window.TotalMilliseconds < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        // Bound key size and keep raw account/IP identifiers out of Redis keys.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(partition)));
        var result = (RedisResult[])(await redis.GetDatabase().ScriptEvaluateAsync(AcquireScript,
            [prefix + policy + ":" + hash], [limit, (long)window.TotalMilliseconds]).WaitAsync(cancellationToken))!;
        return new((long)result[0] == 1, TimeSpan.FromMilliseconds((long)result[1]));
    }
}
