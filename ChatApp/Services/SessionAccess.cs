using System.Security.Claims;
using ChatApp.Data;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public static class SessionAccess
{
    // Refresh and every revocation take this same transaction-scoped lock, on every API instance.
    public static Task LockAsync(AppDbContext db, Guid userId, CancellationToken ct = default) =>
        ConversationLock.AcquireKeyAsync(db, "auth-user:" + userId, ct);

    public static IQueryable<RefreshToken> Active(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        return db.RefreshTokens.Where(t => t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now &&
            t.User.IsActive && t.User.DeletedAt == null &&
            !db.RefreshTokens.Any(old => old.UserId == t.UserId && old.FamilyId == t.FamilyId && old.RevokedAt != null));
    }

    public static Task<bool> IsActiveAsync(AppDbContext db, ClaimsPrincipal? principal, CancellationToken ct = default) =>
        Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var user) &&
        Guid.TryParse(principal?.FindFirstValue("sid"), out var session)
            ? Active(db).AnyAsync(t => t.UserId == user && t.FamilyId == session, ct)
            : Task.FromResult(false);

    public static async Task<string[]> RecipientSessionsAsync(AppDbContext db, IEnumerable<string> users, CancellationToken ct = default)
    {
        var ids = users.Select(Guid.Parse).Distinct().ToArray();
        var sessions = await Active(db).Where(t => ids.Contains(t.UserId)).Select(t => t.FamilyId).Distinct().ToArrayAsync(ct);
        return sessions.Select(id => id.ToString()).ToArray();
    }
}
