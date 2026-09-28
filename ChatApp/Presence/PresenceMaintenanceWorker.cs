using Microsoft.Extensions.Options;

namespace ChatApp.Presence;

public sealed class PresenceMaintenanceWorker(RedisPresenceStore store, IServiceScopeFactory scopes,
    IOptions<PresenceOptions> options, ILogger<PresenceMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.SweepSeconds));
        do
        {
            try
            {
                await store.SweepExpiredAsync(stoppingToken);
                foreach (var change in await store.PendingAsync(stoppingToken))
                {
                    // A new revision arriving during publication remains queued for the next pass.
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<PresenceEvents>().PublishAsync(change.UserId, stoppingToken);
                    await store.AcknowledgeAsync(change.UserId, change.Revision);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Presence maintenance failed; pending changes will be retried"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
