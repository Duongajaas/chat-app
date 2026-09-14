using System.Text;
using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Realtime;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public class ConversationService(AppDbContext db) : IConversationService
{
    public async Task<ConversationSummaryResponse> GetOrCreateDirectConversationAsync(Guid userId, Guid otherUserId)
    {
        if (userId == otherUserId || otherUserId == Guid.Empty) throw AppException.BadRequest("Người nhận không hợp lệ.");
        if (!await db.Users.AnyAsync(u => u.Id == otherUserId && u.IsActive && u.DeletedAt == null))
            throw AppException.NotFound("Người dùng không tồn tại.");
        var low = userId.CompareTo(otherUserId) < 0 ? userId : otherUserId;
        var high = low == userId ? otherUserId : userId;
        await using var tx = await db.Database.BeginTransactionAsync();
        await ConversationLock.AcquireKeyAsync(db, $"direct:{low}:{high}");
        var existing = await db.DirectConversations.SingleOrDefaultAsync(d => d.UserLowId == low && d.UserHighId == high);
        Guid id;
        if (existing != null)
        {
            id = existing.ConversationId;
            await ConversationLock.AcquireAsync(db, id);
            var member = await db.ConversationMembers.SingleAsync(m => m.ConversationId == id && m.UserId == userId);
            member.HiddenAt = null;
            // Repair direct memberships hidden by the previous implementation; groups use explicit leave.
            member.LeftAt = null;
            member.LeftAtSequence = null;
        }
        else
        {
            var conversation = new Conversation { Type = ConversationType.Direct, CreatedBy = userId };
            id = conversation.Id;
            db.Conversations.Add(conversation);
            db.DirectConversations.Add(new DirectConversation { ConversationId = id, UserLowId = low, UserHighId = high });
            db.ConversationMembers.AddRange(new ConversationMember { ConversationId = id, UserId = userId },
                new ConversationMember { ConversationId = id, UserId = otherUserId });
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await GetConversationByIdAsync(userId, id);
    }

    public Task<bool> IsMemberAsync(Guid userId, Guid conversationId) =>
        db.ConversationMembers.AnyAsync(m => m.UserId == userId && m.ConversationId == conversationId && m.LeftAt == null);

    public async Task<ConversationPageResponse> GetMyConversationsAsync(Guid userId, string? cursor = null, int limit = 30)
    {
        var query = db.Conversations.AsNoTracking().Where(c => c.DeletedAt == null && db.ConversationMembers.Any(m =>
            m.ConversationId == c.Id && m.UserId == userId && (m.LeftAt != null || m.HiddenAt == null)));
        if (cursor != null)
        {
            try
            {
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
                var time = new DateTime(long.Parse(parts[0]), DateTimeKind.Utc);
                var id = Guid.Parse(parts[1]);
                query = query.Where(c => c.CreatedAt < time || (c.CreatedAt == time && c.Id.CompareTo(id) < 0));
            }
            catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException or OverflowException)
            { throw AppException.BadRequest("Cursor không hợp lệ."); }
        }
        // Immutable ordering avoids moving rows across pages when a message arrives.
        var size = Math.Clamp(limit, 1, 100);
        var rows = await query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).Take(size + 1).ToListAsync();
        var more = rows.Count > size;
        if (more) rows.RemoveAt(size);
        var items = await SummariesAsync(userId, rows);
        var next = more ? Convert.ToBase64String(Encoding.UTF8.GetBytes($"{rows[^1].CreatedAt.Ticks}|{rows[^1].Id}")) : null;
        return new(items, next);
    }

    private async Task<List<ConversationSummaryResponse>> SummariesAsync(Guid userId, List<Conversation> conversations)
    {
        var ids = conversations.Select(c => c.Id).ToArray();
        var members = await db.ConversationMembers.AsNoTracking().Where(m => ids.Contains(m.ConversationId) && m.UserId == userId)
            .ToDictionaryAsync(m => m.ConversationId);
        var peers = await db.DirectConversations.Where(d => ids.Contains(d.ConversationId))
            .Select(d => new { d.ConversationId, PeerId = d.UserLowId == userId ? d.UserHighId : d.UserLowId }).ToListAsync();
        var peerIds = peers.Select(p => p.PeerId).ToArray();
        var names = await db.Users.Where(u => peerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        var blocked = await db.Blocks.Where(b => (b.BlockerId == userId && peerIds.Contains(b.BlockedId)) ||
            (b.BlockedId == userId && peerIds.Contains(b.BlockerId)))
            .Select(b => b.BlockerId == userId ? b.BlockedId : b.BlockerId).ToListAsync();
        var latest = await db.Messages.Where(m => ids.Contains(m.ConversationId) &&
            !db.MessageDeletions.Any(d => d.MessageId == m.Id && d.UserId == userId) &&
            (db.DirectConversations.Any(d => d.ConversationId == m.ConversationId) ||
             db.ConversationMembershipPeriods.Any(p => p.ConversationId == m.ConversationId && p.UserId == userId &&
                 m.Sequence > p.StartSequence && (p.EndSequence == null || m.Sequence <= p.EndSequence))))
            .GroupBy(m => m.ConversationId).Select(g => g.OrderByDescending(m => m.Sequence).First()).ToListAsync();
        var previews = latest.ToDictionary(m => m.ConversationId);
        var counts = await db.ConversationMembers.Where(m => ids.Contains(m.ConversationId) && m.LeftAt == null)
            .GroupBy(m => m.ConversationId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count);
        return conversations.Select(c =>
        {
            var member = members[c.Id];
            var peer = peers.FirstOrDefault(p => p.ConversationId == c.Id)?.PeerId;
            var last = previews.GetValueOrDefault(c.Id);
            var active = member.LeftAt == null && c.ClosedAt == null;
            return new ConversationSummaryResponse(c.Id, peer.HasValue ? names.GetValueOrDefault(peer.Value) : c.Name,
                c.Type, member.UnreadCount, c.CreatedAt, c.UpdatedAt, last?.Id, last?.CreatedAt,
                peer.HasValue && blocked.Contains(peer.Value), peer,
                last == null ? null : last.DeletedAt != null ? "Tin nhắn đã được thu hồi" : last.Content,
                active && member.Role is MemberRole.Admin or MemberRole.Owner, member.HiddenAt != null, member.LeftAt != null,
                active ? member.Role : null, active && c.Type == ConversationType.Group ? GroupPermissionMatrix.Permissions(member.Role) : [],
                active ? counts.GetValueOrDefault(c.Id) : 0, c.Version, c.ClosedAt);
        }).ToList();
    }

    public async Task<ConversationSummaryResponse> GetConversationByIdAsync(Guid userId, Guid conversationId)
    {
        if (!await db.ConversationMembers.AnyAsync(m => m.UserId == userId && m.ConversationId == conversationId))
            throw AppException.Forbidden("Không có quyền truy cập hội thoại.");
        var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == conversationId)
            ?? throw AppException.NotFound("Hội thoại không tồn tại.");
        return (await SummariesAsync(userId, [conversation]))[0];
    }

    public async Task<ConversationSummaryResponse> CreateConversationAsync(Guid userId, CreateConversationRequest request, Guid? key = null)
    {
        if (request.Type != ConversationType.Group || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100 || request.MemberIds == null)
            throw AppException.BadRequest("Thông tin nhóm không hợp lệ.");
        var ids = request.MemberIds.Append(userId).Distinct().ToArray();
        if (ids.Length is < 2 or > 100 || ids.Contains(Guid.Empty)) throw AppException.BadRequest("Nhóm cần 2 đến 100 thành viên.");
        if (await db.Users.CountAsync(u => ids.Contains(u.Id) && u.IsActive && u.DeletedAt == null) != ids.Length)
            throw AppException.BadRequest("Có thành viên không tồn tại.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await ConversationLock.AcquireKeyAsync(db, "presence-privacy");
        var requestHash = GroupService.Hash(System.Text.Json.JsonSerializer.Serialize(new { request.Name, Ids = ids.Order().ToArray() }));
        if (key.HasValue)
        {
            var receipt = await db.GroupOperations.SingleOrDefaultAsync(o => o.ActorId == userId && o.Operation == "create" && o.Key == key);
            if (receipt != null)
            {
                if (receipt.RequestHash != requestHash) throw AppException.Conflict("Idempotency-Key đã dùng cho yêu cầu khác.");
                return await GetConversationByIdAsync(userId, receipt.ResultId);
            }
        }
        foreach (var target in ids.Where(id => id != userId)) await GroupService.ValidateFriendAsync(db, userId, target);
        var conversation = new Conversation { Type = ConversationType.Group, Name = request.Name.Trim(), CreatedBy = userId };
        db.Conversations.Add(conversation);
        db.ConversationMembers.AddRange(ids.Select(id => new ConversationMember { ConversationId = conversation.Id, UserId = id,
            Role = id == userId ? MemberRole.Owner : MemberRole.Member }));
        db.ConversationMembershipPeriods.AddRange(ids.Select(id => new ConversationMembershipPeriod {
            ConversationId = conversation.Id, UserId = id, StartSequence = 0 }));
        new GroupService(db).Changed(conversation, userId, "CREATE_GROUP");
        if (key.HasValue) db.GroupOperations.Add(new GroupOperation { ActorId = userId, Operation = "create",
            Key = key.Value, RequestHash = requestHash, ResultId = conversation.Id });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await GetConversationByIdAsync(userId, conversation.Id);
    }

    public async Task DeleteConversationAsync(Guid userId, Guid conversationId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ConversationLock.AcquireAsync(db, conversationId);
        var member = await db.ConversationMembers.SingleOrDefaultAsync(m => m.UserId == userId && m.ConversationId == conversationId)
            ?? throw AppException.Forbidden("Không có quyền truy cập hội thoại.");
        member.HiddenAt = DateTime.UtcNow;
        ConversationEvents.Add(db, conversationId, "ConversationHidden", new { ConversationId = conversationId }, userId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public Task LeaveConversationAsync(Guid userId, Guid conversationId) =>
        new GroupService(db).RemoveAsync(userId, conversationId, userId, false);
}
