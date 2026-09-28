using ChatApp.Options;
using ChatApp.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace ChatApp.Tests;

public class RedisRateLimitMiddlewareTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Redis_outage_returns_503_without_executing_the_endpoint(bool timedOut)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EnableRateLimitingAttribute("chat-write")), "test"));
        var counter = new Mock<IRateLimitCounter>();
        counter.Setup(c => c.AcquireAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(timedOut ? new RedisTimeoutException("private connection details", CommandStatus.Unknown)
                : new RedisConnectionException(ConnectionFailureType.SocketFailure, "private connection details"));
        var executed = false;
        var middleware = new RedisRateLimitMiddleware(_ => { executed = true; return Task.CompletedTask; },
            NullLogger<RedisRateLimitMiddleware>.Instance);
        await middleware.InvokeAsync(context, counter.Object, Microsoft.Extensions.Options.Options.Create(new RateLimitingOptions()));
        Assert.False(executed);
        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("5", context.Response.Headers.RetryAfter.ToString());
        context.Response.Body.Position = 0;
        Assert.DoesNotContain("private connection details", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
}
