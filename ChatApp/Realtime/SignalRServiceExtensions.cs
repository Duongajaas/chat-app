using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace ChatApp.Realtime;

public static class SignalRServiceExtensions
{
    public static IServiceCollection AddChatSignalR(this IServiceCollection services, IConfiguration configuration)
    {
        var signalR = services.AddSignalR(options =>
        {
            options.MaximumReceiveMessageSize = 256 * 1024;
            options.AddFilter<SessionHubFilter>();
            options.AddFilter<HubRateLimitFilter>();
        });

        services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

        // Local development and existing tests can still run without Redis.
        if (!configuration.GetValue<bool>("Redis:Enabled")) return services;

        var connectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Redis is required when Redis is enabled.");
        var prefix = configuration["Redis:ChannelPrefix"];
        if (string.IsNullOrWhiteSpace(prefix))
            throw new InvalidOperationException("Redis:ChannelPrefix is required when Redis is enabled.");

        var redis = ConfigurationOptions.Parse(connectionString);
        redis.AbortOnConnectFail = false;
        redis.ChannelPrefix = RedisChannel.Literal(prefix);
        signalR.AddStackExchangeRedis(options => options.Configuration = redis.Clone());

        // A separate, DI-owned connection probes Redis without publishing chat events.
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
        services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis", tags: ["ready"],
            timeout: TimeSpan.FromSeconds(2));
        return services;
    }
}

