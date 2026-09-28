using ChatApp.Realtime;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace ChatApp.Tests;

public class RedisHealthCheckTests
{
    [Fact]
    public async Task Disconnected_redis_makes_readiness_unhealthy()
    {
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.SetupGet(r => r.IsConnected).Returns(false);
        var result = await new RedisHealthCheck(redis.Object).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Failed_ping_does_not_report_healthy_or_expose_connection_details()
    {
        var database = new Mock<IDatabase>();
        database.Setup(d => d.PingAsync(CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "private endpoint details"));
        var redis = new Mock<IConnectionMultiplexer>();
        redis.SetupGet(r => r.IsConnected).Returns(true);
        redis.Setup(r => r.GetDatabase(-1, null)).Returns(database.Object);
        var result = await new RedisHealthCheck(redis.Object).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Redis unavailable", result.Description);
        Assert.Null(result.Exception);
    }
}
