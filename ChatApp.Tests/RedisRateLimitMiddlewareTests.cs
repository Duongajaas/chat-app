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

    [Fact]
    public async Task Rate_limited_request_returns_429_with_retry_after_header()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EnableRateLimitingAttribute("chat-write")), "test"));
        var counter = new Mock<IRateLimitCounter>();
        counter.Setup(c => c.AcquireAsync("chat-write", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitDecision(false, TimeSpan.FromSeconds(42.3)));
        var executed = false;
        var middleware = new RedisRateLimitMiddleware(_ => { executed = true; return Task.CompletedTask; }, NullLogger<RedisRateLimitMiddleware>.Instance);
        await middleware.InvokeAsync(context, counter.Object, Microsoft.Extensions.Options.Options.Create(new RateLimitingOptions()));
        Assert.False(executed);
        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("43", context.Response.Headers.RetryAfter.ToString());
    }

    [Theory]
    [InlineData("friend-request", 20)]
    [InlineData("report", 10)]
    public async Task Dedicated_policies_are_enforced_for_friend_request_and_report(string policy, int expectedLimit)
    {
        var userId = Guid.NewGuid().ToString();
        var context = new DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId)
        ], "Test"));
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EnableRateLimitingAttribute(policy)), "test"));

        string? capturedPolicy = null;
        string? capturedPartition = null;
        int capturedLimit = 0;

        var counter = new Mock<IRateLimitCounter>();
        counter.Setup(c => c.AcquireAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, int, TimeSpan, CancellationToken>((p, part, lim, _, _) =>
            {
                if (capturedPolicy == null)
                {
                    capturedPolicy = p;
                    capturedPartition = part;
                    capturedLimit = lim;
                }
            })
            .ReturnsAsync(new RateLimitDecision(true, TimeSpan.Zero));

        var executed = false;
        var middleware = new RedisRateLimitMiddleware(_ => { executed = true; return Task.CompletedTask; }, NullLogger<RedisRateLimitMiddleware>.Instance);
        await middleware.InvokeAsync(context, counter.Object, Microsoft.Extensions.Options.Options.Create(new RateLimitingOptions()));

        Assert.True(executed);
        Assert.Equal(policy, capturedPolicy);
        Assert.Equal("user:" + userId, capturedPartition);
        Assert.Equal(expectedLimit, capturedLimit);
    }

    [Fact]
    public async Task Hub_rate_limit_filter_throws_hub_exception_when_limit_exceeded()
    {
        var counter = new Mock<IRateLimitCounter>();
        counter.Setup(c => c.AcquireAsync("hub", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitDecision(false, TimeSpan.FromSeconds(5)));

        var filter = new ChatApp.Realtime.HubRateLimitFilter(counter.Object);
        var hubContext = new Mock<Microsoft.AspNetCore.SignalR.HubCallerContext>();
        hubContext.SetupGet(h => h.ConnectionId).Returns("conn-1");
        hubContext.SetupGet(h => h.ConnectionAborted).Returns(CancellationToken.None);

        var invocation = new Microsoft.AspNetCore.SignalR.HubInvocationContext(hubContext.Object, Mock.Of<IServiceProvider>(), Mock.Of<Microsoft.AspNetCore.SignalR.Hub>(), typeof(Microsoft.AspNetCore.SignalR.Hub).GetMethods()[0], Array.Empty<object?>());

        var ex = await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(async () =>
        {
            await filter.InvokeMethodAsync(invocation, _ => ValueTask.FromResult<object?>(null));
        });

        Assert.Equal("Bạn thao tác quá nhanh. Vui lòng thử lại.", ex.Message);
    }
}
