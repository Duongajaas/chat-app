using System.Collections.Concurrent;
using ChatApp.Data;
using ChatApp.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Realtime;

public sealed class SessionConnectionMonitor(IServiceScopeFactory scopes, ILogger<SessionConnectionMonitor> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, HubCallerContext> connections = new();
    public void Add(HubCallerContext context) => connections[context.ConnectionId] = context;
    public void Remove(string id) => connections.TryRemove(id, out _);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var snapshot = connections.Values.ToArray();
            if (snapshot.Length == 0) continue;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                foreach (var batch in snapshot.Chunk(200))
                {
                    var ids = batch.Select(c => Guid.TryParse(c.UserIdentifier, out var id) ? id : Guid.Empty).Distinct().ToArray();
                    var active = (await SessionAccess.Active(db).Where(t => ids.Contains(t.FamilyId)).Select(t => t.FamilyId).Distinct().ToArrayAsync(stoppingToken)).ToHashSet();
                    foreach (var context in batch)
                        if (!Guid.TryParse(context.UserIdentifier, out var id) || !active.Contains(id)) context.Abort();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Unable to validate realtime sessions; closing local connections.");
                foreach (var context in snapshot) context.Abort();
            }
        }
    }
}
