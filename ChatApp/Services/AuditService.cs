using System.Text.Json;
using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public interface IAuditService
{
    Task LogAsync(Guid? conversationId, Guid actorId, string action, Guid? targetUserId = null, object? metadata = null, CancellationToken ct = default);
    Task<List<AuditLogResponse>> GetModerationLogsAsync(Guid actorId, int limit = 50, CancellationToken ct = default);
}

public class AuditService(AppDbContext db) : IAuditService
{
    public async Task LogAsync(Guid? conversationId, Guid actorId, string action, Guid? targetUserId = null, object? metadata = null, CancellationToken ct = default)
    {
        var sanitizedMetadata = metadata != null ? JsonSerializer.Serialize(metadata) : null;
        db.AuditLogs.Add(new AuditLog
        {
            ConversationId = conversationId,
            ActorId = actorId,
            Action = action,
            TargetUserId = targetUserId,
            Metadata = sanitizedMetadata,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<AuditLogResponse>> GetModerationLogsAsync(Guid actorId, int limit = 50, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == actorId && u.IsActive && u.DeletedAt == null, ct)
            ?? throw AppException.Forbidden("Tài khoản không hợp lệ.");
        if (user.Role is not (UserRole.Moderator or UserRole.Admin))
            throw AppException.Forbidden("Chỉ kiểm duyệt viên hoặc quản trị viên mới có quyền xem nhật ký này.");

        var size = Math.Clamp(limit, 1, 100);
        var logs = await db.AuditLogs.AsNoTracking()
            .Where(l => l.ConversationId == null || l.Action.StartsWith("MODERATOR_") || l.Action.Contains("RESTRICT"))
            .OrderByDescending(l => l.CreatedAt)
            .Take(size)
            .ToListAsync(ct);

        var actorIds = logs.Where(l => l.ActorId != null).Select(l => l.ActorId!.Value).Distinct().ToArray();
        var targetIds = logs.Where(l => l.TargetUserId != null).Select(l => l.TargetUserId!.Value).Distinct().ToArray();
        var userIds = actorIds.Concat(targetIds).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Username }).ToDictionaryAsync(u => u.Id, ct);

        return logs.Select(l => new AuditLogResponse(
            l.Id,
            l.ConversationId,
            l.ActorId,
            l.ActorId != null && users.TryGetValue(l.ActorId.Value, out var act) ? act.FullName : null,
            l.ActorId != null && users.TryGetValue(l.ActorId.Value, out var actU) ? actU.Username : null,
            l.Action,
            l.TargetUserId,
            l.TargetUserId != null && users.TryGetValue(l.TargetUserId.Value, out var tgt) ? tgt.FullName : null,
            l.TargetUserId != null && users.TryGetValue(l.TargetUserId.Value, out var tgtU) ? tgtU.Username : null,
            l.Metadata,
            l.CreatedAt
        )).ToList();
    }
}
