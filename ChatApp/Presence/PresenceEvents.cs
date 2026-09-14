using ChatApp.Data;
using ChatApp.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Presence;

public sealed class PresenceEvents(AppDbContext db, IPresenceTracker presence, IHubContext<ChatHub> hub)
{
    // Redis writes queue transitions atomically; the worker publishes them. Local development publishes directly.
    public Task NotifyLocalAsync(Guid userId) => presence is RedisPresenceTracker ? Task.CompletedTask : PublishAsync(userId);

    private IQueryable<Guid> Audience(Guid subject) => db.Users.Where(u => u.Id != subject && u.IsActive && u.DeletedAt == null &&
        !db.Blocks.Any(b => (b.BlockerId == subject && b.BlockedId == u.Id) || (b.BlockerId == u.Id && b.BlockedId == subject)) &&
        (db.Friendships.Any(f => (f.UserLowId == subject && f.UserHighId == u.Id) || (f.UserHighId == subject && f.UserLowId == u.Id)) ||
         db.ConversationMembers.Any(m => m.UserId == u.Id && m.LeftAt == null && db.ConversationMembers.Any(other =>
             other.UserId == subject && other.LeftAt == null && other.ConversationId == m.ConversationId))))
        .Select(u => u.Id);

    public async Task PublishAsync(Guid userId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ConversationLock.AcquireKeyAsync(db, "presence-privacy", ct);
        var subject = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (subject is null || subject.HideLastSeen || !subject.IsActive || subject.DeletedAt != null) return;
        var online = await presence.IsOnlineAsync(userId);
        if (!online)
        {
            subject.LastSeenAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        var audience = await Audience(userId).Select(id => id.ToString()).ToArrayAsync(ct);
        await hub.Clients.Users(audience).SendAsync("UserPresenceChanged", new
        { UserId = userId, IsOnline = online, LastSeenAt = online ? null : subject.LastSeenAt }, ct);
        await tx.CommitAsync(ct);
    }

    public async Task SendSnapshotAsync(Guid userId, IClientProxy client, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ConversationLock.AcquireKeyAsync(db, "presence-privacy", ct);
        var allowed = Audience(userId);
        var candidates = await db.Users.Where(u => allowed.Contains(u.Id) && !u.HideLastSeen).Select(u => u.Id).ToListAsync(ct);
        var online = new List<Guid>();
        foreach (var id in candidates) if (await presence.IsOnlineAsync(id)) online.Add(id);
        await client.SendAsync("PresenceSnapshot", online, ct);
        await tx.CommitAsync(ct);
    }
}
