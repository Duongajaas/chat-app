using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace ChatApp.Realtime;

public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!redis.IsConnected) return HealthCheckResult.Unhealthy("Redis unavailable");
        try
        {
            await redis.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Redis unavailable");
        }
    }
}
