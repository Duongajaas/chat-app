using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data;

// All conversation mutations and delivery use this transaction-scoped lock.
// Acquire before inserting messages: identity allocation then follows commit order per conversation.
public static class ConversationLock
{
    public static Task AcquireAsync(AppDbContext db, Guid id, CancellationToken ct = default) =>
        AcquireKeyAsync(db, "conversation:" + id, ct);

    public static Task AcquireKeyAsync(AppDbContext db, string key, CancellationToken ct = default) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
}
