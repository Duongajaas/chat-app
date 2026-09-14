using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Realtime;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public class GroupService(AppDbContext db)
{
    public async Task<(Conversation Group, ConversationMember Actor)> AuthorizeAsync(Guid actorId, Guid id, string? permission = null)
    {
        var actor = await db.ConversationMembers.SingleOrDefaultAsync(m => m.ConversationId == id && m.UserId == actorId && m.LeftAt == null)
            ?? throw AppException.Forbidden("Bạn không phải thành viên đang hoạt động.");
        var group = await db.Conversations.SingleOrDefaultAsync(c => c.Id == id && c.Type == ConversationType.Group && c.DeletedAt == null)
            ?? throw AppException.NotFound("Nhóm không tồn tại.");
        if (group.ClosedAt != null) throw AppException.Conflict("Nhóm đã đóng.");
        if (permission != null && !GroupPermissionMatrix.HasPermission(actor.Role, permission))
            throw AppException.Forbidden("Bạn không có quyền thực hiện thao tác này.");
        return (group, actor);
    }

    public static async Task ValidateFriendAsync(AppDbContext db, Guid actor, Guid target)
    {
        if (actor == target || !await db.Users.AnyAsync(u => u.Id == target && u.IsActive && u.DeletedAt == null))
            throw AppException.BadRequest("Thành viên không hợp lệ.");
        if (!await db.Friendships.AnyAsync(f => f.UserLowId == actor && f.UserHighId == target || f.UserLowId == target && f.UserHighId == actor) ||
            await db.Blocks.AnyAsync(b => b.BlockerId == actor && b.BlockedId == target || b.BlockerId == target && b.BlockedId == actor))
            throw AppException.Forbidden("Chỉ có thể thêm bạn bè không bị chặn.");
    }

    public void Audit(Conversation group, Guid actor, string action, Guid? target = null, object? metadata = null) =>
        db.AuditLogs.Add(new AuditLog { ConversationId = group.Id, ActorId = actor, Action = action, TargetUserId = target,
            Metadata = JsonSerializer.Serialize(new { group.Version, details = metadata }) });

    public void Changed(Conversation group, Guid actor, string action, Guid? target = null, object? metadata = null)
    {
        group.Version++;
        group.UpdatedAt = DateTime.UtcNow;
        Audit(group, actor, action, target, metadata);
        ConversationEvents.Add(db, group.Id, "ConversationChanged", new { ConversationId = group.Id, group.Version, Reason = action });
    }

    public async Task<List<GroupMemberResponse>> MembersAsync(Guid actorId, Guid id)
    {
        var (_, actor) = await AuthorizeAsync(actorId, id);
        var members = await (from m in db.ConversationMembers.AsNoTracking()
            join u in db.Users on m.UserId equals u.Id
            where m.ConversationId == id && m.LeftAt == null
            orderby m.Role, m.JoinedAt, m.Id
            select new { m.UserId, u.FullName, u.AvatarUrl, m.Role, m.JoinedAt }).Take(100).ToListAsync();
        return members.Select(m => new GroupMemberResponse(m.UserId, m.FullName, m.AvatarUrl, m.Role, m.JoinedAt,
            m.UserId != actorId && GroupPermissionMatrix.CanKick(actor.Role, m.Role),
            m.UserId != actorId && actor.Role == MemberRole.Owner && m.Role != MemberRole.Owner)).ToList();
    }

    public async Task RenameAsync(Guid actorId, Guid id, string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 100) throw AppException.BadRequest("Tên nhóm cần 1–100 ký tự.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await ConversationLock.AcquireAsync(db, id);
        var (group, _) = await AuthorizeAsync(actorId, id, "RenameGroup");
        if (group.Name == name) return;
        var oldName = group.Name;
        group.Name = name;
        Changed(group, actorId, "RENAME_GROUP", metadata: new { oldName, name });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task SetRoleAsync(Guid actorId, Guid id, Guid targetId, MemberRole role)
    {
        if (role is not (MemberRole.Admin or MemberRole.Member)) throw AppException.BadRequest("Chỉ được cấp hoặc hạ Admin.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await ConversationLock.AcquireAsync(db, id);
        var (group, _) = await AuthorizeAsync(actorId, id, "ManageRoles");
        var target = await db.ConversationMembers.SingleOrDefaultAsync(m => m.ConversationId == id && m.UserId == targetId && m.LeftAt == null)
            ?? throw AppException.NotFound("Thành viên không tồn tại.");
        if (actorId == targetId || target.Role == MemberRole.Owner) throw AppException.Forbidden("Không được thay đổi Owner.");
        if (target.Role == role) return;
        target.Role = role;
        Changed(group, actorId, role == MemberRole.Admin ? "PROMOTE_ADMIN" : "DEMOTE_ADMIN", targetId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task AddAsync(Guid actorId, Guid id, Guid targetId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        // Same ordering as BlockService; membership affects presence privacy too.
        await ConversationLock.AcquireKeyAsync(db, "presence-privacy");
        await ConversationLock.AcquireAsync(db, id);
        var (group, actor) = await AuthorizeAsync(actorId, id, "AddMember");
        var member = await db.ConversationMembers.SingleOrDefaultAsync(m => m.ConversationId == id && m.UserId == targetId);
        if (member != null && member.LeftAt == null) return;
        if (member?.LeaveReason == "Kicked" && actor.Role == MemberRole.Member)
            throw AppException.Forbidden("Chỉ Owner hoặc Admin được thêm lại người bị kick.");
        await ValidateFriendAsync(db, actorId, targetId);
        await ActivateAsync(id, targetId, member);
        Changed(group, actorId, "ADD_MEMBER", targetId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // Caller holds the conversation lock and owns the transaction.
    public async Task ActivateAsync(Guid id, Guid userId, ConversationMember? member)
    {
        if (await db.ConversationMembers.CountAsync(m => m.ConversationId == id && m.LeftAt == null) >= 100)
            throw AppException.Conflict("Nhóm đã đủ 100 thành viên.");
        var boundary = await db.Messages.Where(m => m.ConversationId == id).MaxAsync(m => (long?)m.Sequence) ?? 0;
        if (member == null)
        {
            member = new ConversationMember { ConversationId = id, UserId = userId };
            db.ConversationMembers.Add(member);
        }
        member.Role = MemberRole.Member;
        member.JoinedAt = DateTime.UtcNow;
        member.LeftAt = member.HiddenAt = null;
        member.LeftAtSequence = null;
        member.LeaveReason = null;
        member.RemovedByUserId = null;
        member.LastReadSequence = boundary;
        member.LastReadMessageId = null;
        member.LastReadAt = null;
        member.UnreadCount = 0;
        db.ConversationMembershipPeriods.Add(new ConversationMembershipPeriod {
            ConversationId = id, UserId = userId, StartSequence = boundary, JoinedAt = member.JoinedAt });
    }

    public async Task RemoveAsync(Guid actorId, Guid id, Guid targetId, bool kick)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ConversationLock.AcquireKeyAsync(db, "presence-privacy");
        await ConversationLock.AcquireAsync(db, id);
        var target = await db.ConversationMembers.SingleOrDefaultAsync(m => m.ConversationId == id && m.UserId == targetId)
            ?? throw AppException.Forbidden("Không phải thành viên.");
        // Idempotent own leave remains valid after the last member closes the group.
        if (!kick && actorId == targetId && target.LeftAt != null) return;
        var (group, actor) = await AuthorizeAsync(actorId, id);
        if (kick && (actorId == targetId || !GroupPermissionMatrix.CanKick(actor.Role, target.Role)))
            throw AppException.Forbidden("Không có quyền kick thành viên này.");
        if (target.LeftAt != null) return;
        if (!kick && actorId != targetId) throw AppException.Forbidden("Không có quyền rời thay người khác.");
        var wasOwner = target.Role == MemberRole.Owner;
        target.Role = MemberRole.Member;
        var cutoff = await db.Messages.Where(m => m.ConversationId == id).MaxAsync(m => (long?)m.Sequence) ?? 0;
        target.LeftAtSequence = cutoff;
        target.LeftAt = target.HiddenAt = DateTime.UtcNow;
        target.LeaveReason = kick ? "Kicked" : "Left";
        target.RemovedByUserId = kick ? actorId : null;
        target.UnreadCount = 0;
        var period = await db.ConversationMembershipPeriods.SingleOrDefaultAsync(p => p.ConversationId == id && p.UserId == targetId && p.EndSequence == null);
        if (period != null) { period.EndSequence = cutoff; period.LeftAt = target.LeftAt; }
        // Flush demotion before promotion to respect the partial unique Owner index.
        await db.SaveChangesAsync();
        Changed(group, actorId, kick ? "KICK_MEMBER" : "LEAVE_GROUP", targetId);
        if (wasOwner)
        {
            var successor = await db.ConversationMembers.Where(m => m.ConversationId == id && m.LeftAt == null)
                .OrderBy(m => m.Role == MemberRole.Admin ? 0 : 1).ThenBy(m => m.JoinedAt).ThenBy(m => m.Id).FirstOrDefaultAsync();
            if (successor != null)
            {
                successor.Role = MemberRole.Owner;
                Audit(group, actorId, "TRANSFER_OWNERSHIP", successor.UserId);
            }
            else
            {
                group.ClosedAt = DateTime.UtcNow; group.CloseReason = "Empty";
                await db.ConversationInvites.Where(i => i.ConversationId == id && i.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, group.ClosedAt));
                Audit(group, actorId, "CLOSE_GROUP", metadata: new { reason = "Empty" });
            }
        }
        ConversationEvents.Add(db, id, "ConversationLeft", new { ConversationId = id, group.Version, Reason = target.LeaveReason }, targetId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<List<GroupInviteResponse>> InvitesAsync(Guid actorId, Guid id)
    {
        await AuthorizeAsync(actorId, id, "ManageInvites");
        return await db.ConversationInvites.Where(i => i.ConversationId == id)
            .OrderByDescending(i => i.CreatedAt).Take(100)
            .Select(i => new GroupInviteResponse(i.Id, i.CreatedBy, i.MaxUses, i.UsedCount, i.ExpiresAt, i.RevokedAt, null)).ToListAsync();
    }

    public async Task<GroupInviteResponse> CreateInviteAsync(Guid actorId, Guid id, CreateGroupInviteRequest request, Guid? key = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ConversationLock.AcquireAsync(db, id);
        var (group, _) = await AuthorizeAsync(actorId, id, "ManageInvites");
        var requestHash = Hash(JsonSerializer.Serialize(new { id, request }));
        if (key.HasValue)
        {
            await ConversationLock.AcquireKeyAsync(db, $"invite-request:{actorId}:{key}");
            var receipt = await db.GroupOperations.SingleOrDefaultAsync(o => o.ActorId == actorId && o.Operation == "invite" && o.Key == key);
            if (receipt != null)
            {
                if (receipt.RequestHash != requestHash) throw AppException.Conflict("Idempotency-Key đã dùng cho yêu cầu khác.");
                var previous = await db.ConversationInvites.SingleAsync(i => i.Id == receipt.ResultId);
                return new(previous.Id, previous.CreatedBy, previous.MaxUses, previous.UsedCount, previous.ExpiresAt, previous.RevokedAt);
            }
        }
        var now = await db.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();
        var expires = request.ExpiresAt?.UtcDateTime ?? now.AddDays(7);
        if (request.MaxUses is < 1 or > 10000 || expires <= now || expires > now.AddDays(30))
            throw AppException.BadRequest("Lượt dùng phải từ 1–10000 và thời hạn tối đa 30 ngày.");
        if (await db.ConversationInvites.CountAsync(i => i.ConversationId == id && i.RevokedAt == null && (i.ExpiresAt == null || i.ExpiresAt > now)) >= 20)
            throw AppException.Conflict("Tối đa 20 link chưa hết hạn.");
        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var invite = new ConversationInvite { ConversationId = id, CreatedBy = actorId, InviteCodeHash = Hash(code), MaxUses = request.MaxUses, ExpiresAt = expires };
        db.ConversationInvites.Add(invite);
        if (key.HasValue) db.GroupOperations.Add(new GroupOperation { ActorId = actorId, Operation = "invite",
            Key = key.Value, RequestHash = requestHash, ResultId = invite.Id });
        Changed(group, actorId, "CREATE_INVITE", metadata: new { invite.Id });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new(invite.Id, actorId, invite.MaxUses, 0, expires, null, code);
    }

    public async Task RevokeAsync(Guid actorId, Guid id, Guid inviteId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ConversationLock.AcquireAsync(db, id);
        var (group, _) = await AuthorizeAsync(actorId, id, "ManageInvites");
        var invite = await db.ConversationInvites.SingleOrDefaultAsync(i => i.Id == inviteId && i.ConversationId == id)
            ?? throw AppException.NotFound("Link không tồn tại.");
        if (invite.RevokedAt != null) return;
        invite.RevokedAt = DateTime.UtcNow;
        Changed(group, actorId, "REVOKE_INVITE", metadata: new { invite.Id });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<Guid> JoinAsync(Guid userId, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 100) throw AppException.NotFound("Link không hợp lệ.");
        var hash = Hash(code);
        var id = await db.ConversationInvites.Where(i => i.InviteCodeHash == hash).Select(i => (Guid?)i.ConversationId).SingleOrDefaultAsync()
            ?? throw AppException.NotFound("Link không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await ConversationLock.AcquireKeyAsync(db, "presence-privacy");
        await ConversationLock.AcquireAsync(db, id);
        var group = await db.Conversations.SingleAsync(c => c.Id == id);
        var invite = await db.ConversationInvites.SingleAsync(i => i.InviteCodeHash == hash);
        var member = await db.ConversationMembers.SingleOrDefaultAsync(m => m.ConversationId == id && m.UserId == userId);
        if (group.Type != ConversationType.Group || group.ClosedAt != null || group.DeletedAt != null)
            throw AppException.NotFound("Link không hợp lệ.");
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.DeletedAt == null)) throw AppException.Forbidden("Tài khoản không hoạt động.");
        if (member != null && member.LeftAt == null) return id;
        var now = await db.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();
        if (invite.RevokedAt != null || invite.ExpiresAt <= now || invite.MaxUses != null && invite.UsedCount >= invite.MaxUses)
            throw AppException.NotFound("Link không hợp lệ.");
        if (member?.LeaveReason == "Kicked") throw AppException.Forbidden("Bạn cần Owner hoặc Admin thêm lại.");
        await ActivateAsync(id, userId, member);
        var updated = await db.ConversationInvites.Where(i => i.Id == invite.Id && i.RevokedAt == null &&
            (i.ExpiresAt == null || i.ExpiresAt > now) && (i.MaxUses == null || i.UsedCount < i.MaxUses))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedCount, i => i.UsedCount + 1));
        if (updated != 1) throw AppException.NotFound("Link không hợp lệ.");
        Changed(group, userId, "JOIN_VIA_INVITE", userId, new { invite.Id });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return id;
    }

    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
