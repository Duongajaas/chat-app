using ChatApp.Presence;
using ChatApp.RateLimiting;

namespace ChatApp.Realtime;

public static class RuntimeServices
{
    public static string AddChatRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        var role = (configuration["Runtime:Role"] ?? "all").ToLowerInvariant();
        if (role is not ("api" or "worker" or "all"))
            throw new InvalidOperationException("Runtime:Role must be api, worker, or all.");
        var distributed = configuration.GetValue<bool>("Redis:Enabled");
        if (role == "worker" && !distributed)
            throw new InvalidOperationException("A separate worker requires Redis:Enabled.");

        services.AddScoped<PresenceEvents>();
        if (distributed)
        {
            services.AddOptions<PresenceOptions>().BindConfiguration("Presence")
                .Validate(o => o.HeartbeatSeconds > 0 && o.ConnectionTtlSeconds >= o.HeartbeatSeconds * 3
                    && o.SweepSeconds > 0 && o.SweepSeconds <= o.ConnectionTtlSeconds, "Invalid presence timing")
                .ValidateOnStart();
            services.AddSingleton<RedisPresenceStore>();
            services.AddSingleton<RedisPresenceTracker>();
            services.AddSingleton<IPresenceTracker>(sp => sp.GetRequiredService<RedisPresenceTracker>());
            services.AddSingleton<IRateLimitCounter, RedisRateLimitCounter>();
            if (role != "worker") services.AddHostedService(sp => sp.GetRequiredService<RedisPresenceTracker>());
            if (role != "api") services.AddHostedService<PresenceMaintenanceWorker>();
        }
        else
        {
            services.AddSingleton<IPresenceTracker, InMemoryPresenceTracker>();
            services.AddSingleton<IRateLimitCounter, InMemoryRateLimitCounter>();
        }
        if (role != "api") services.AddHostedService<OutboxWorker>();
        return role;
    }
}
