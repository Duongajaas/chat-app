using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Realtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ChatApp.Services;

public class MessageService(AppDbContext db, IBlockService blocks) : IMessageService
{
    public async Task<MessageListResponse> GetMessagesAsync(Guid userId, Guid conversationId, long? before = null,
        long? after = null, int limit = 50, CancellationToken ct = default)
    {
        var member = await ReadableMemberAsync(userId, conversationId, ct);
        if (before.HasValue && after.HasValue) throw AppException.BadRequest("Chỉ dùng before hoặc after.");
        if (before < 0 || after < 0) throw AppException.BadRequest("Cursor không hợp lệ.");
        var size = Math.Clamp(limit, 1, 100);
        var query = VisibleMessages(member).AsNoTracking();
        if (before.HasValue) query = query.Where(m => m.Sequence < before.Value);
        if (after.HasValue) query = query.Where(m => m.Sequence > after.Value);
        var page = await (after.HasValue ? query.OrderBy(m => m.Sequence) : query.OrderByDescending(m => m.Sequence))
            .Take(size + 1).ToListAsync(ct);
        var more = page.Count > size;
        if (more) page.RemoveAt(size);
        if (!after.HasValue) page.Reverse();
        long? cursor = page.Count == 0 ? null : after.HasValue ? page[^1].Sequence : page[0].Sequence;
        var peerRead = await db.ConversationMembers.Where(m => m.ConversationId == conversationId && m.UserId != userId)
            .Select(m => (long?)m.LastReadSequence).MaxAsync(ct) ?? 0;
        if (!await db.DirectConversations.AnyAsync(d => d.ConversationId == conversationId, ct)) peerRead = 0;
        return new MessageListResponse(page.Select(ToResponse).ToList(), cursor, more, peerRead);
    }

    public async Task<MessageResponse> SendMessageAsync(Guid userId, Guid clientMessageId, Guid conversationId,
        SendMessageRequest request, CancellationToken ct = default)
    {
        if (clientMessageId == Guid.Empty) throw AppException.BadRequest("clientMessageId là bắt buộc.");
        var content = request.Content?.Trim();
        if (string.IsNullOrEmpty(content) || content.Length > 4000)
            throw AppException.BadRequest("Tin nhắn phải có từ 1 đến 4000 ký tự.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ConversationLock.AcquireAsync(db, conversationId, ct);
        await ActiveMemberAsync(userId, conversationId, ct);
        var direct = await db.DirectConversations.SingleOrDefaultAsync(d => d.ConversationId == conversationId, ct);
        if (direct != null && await blocks.IsBlockedEitherWayAsync(direct.UserLowId, direct.UserHighId))
            throw AppException.Forbidden("Không thể gửi tin nhắn.");
        var existing = await db.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.SenderId == userId && m.ClientMessageId == clientMessageId, ct);
        if (existing != null) return IdempotentResponse(existing, conversationId, content);
        var message = new Message { ConversationId = conversationId, SenderId = userId, ClientMessageId = clientMessageId, Content = content };
        db.Messages.Add(message);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_messages_sender_id_client_message_id" })
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var winner = await db.Messages.AsNoTracking().SingleAsync(m => m.SenderId == userId && m.ClientMessageId == clientMessageId, ct);
            return IdempotentResponse(winner, conversationId, content);
        }
        await db.ConversationMembers.Where(m => m.ConversationId == conversationId && m.LeftAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.HiddenAt, (DateTime?)null)
                .SetProperty(m => m.UnreadCount, m => m.UnreadCount + (m.UserId == userId ? 0 : 1)), ct);
        await db.Conversations.Where(c => c.Id == conversationId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.LastMessageId, (Guid?)message.Id)
            .SetProperty(c => c.LastMessageAt, (DateTime?)message.CreatedAt)
            .SetProperty(c => c.UpdatedAt, message.CreatedAt), ct);
        var response = ToResponse(message);
        ConversationEvents.Add(db, conversationId, "ReceiveMessage", response);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return response;
    }

    public async Task MarkAsReadAsync(Guid userId, Guid conversationId, long sequence, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ConversationLock.AcquireAsync(db, conversationId, ct);
        var member = await ActiveMemberAsync(userId, conversationId, ct);
        if (sequence <= member.LastReadSequence) return;
        var message = await MessageVisibility.Readable(db, userId, conversationId).SingleOrDefaultAsync(m => m.Sequence == sequence, ct)
            ?? throw AppException.BadRequest("Read cursor không thuộc hội thoại.");
        member.LastReadSequence = sequence;
        member.LastReadMessageId = message.Id;
        member.LastReadAt = DateTime.UtcNow;
        member.UnreadCount = await UnreadAsync(member, ct);
        ConversationEvents.Add(db, conversationId, "MessagesRead", new
        { ConversationId = conversationId, UserId = userId, Sequence = sequence, ReadAt = member.LastReadAt });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteMessageAsync(Guid userId, Guid messageId, CancellationToken ct = default) =>
        await DeleteAsync(userId, messageId, false, ct);

    public async Task DeleteForMeAsync(Guid userId, Guid messageId, CancellationToken ct = default) =>
        await DeleteAsync(userId, messageId, true, ct);

    private async Task DeleteAsync(Guid userId, Guid messageId, bool forMe, CancellationToken ct)
    {
        var conversationId = await db.Messages.Where(m => m.Id == messageId).Select(m => (Guid?)m.ConversationId).SingleOrDefaultAsync(ct)
            ?? throw AppException.NotFound("Tin nhắn không tồn tại.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ConversationLock.AcquireAsync(db, conversationId, ct);
        var member = forMe ? await ReadableMemberAsync(userId, conversationId, ct) : await ActiveMemberAsync(userId, conversationId, ct);
        var message = await db.Messages.SingleAsync(m => m.Id == messageId, ct);
        if (!await MessageVisibility.Readable(db, userId, conversationId).AnyAsync(m => m.Id == messageId, ct))
            throw AppException.Forbidden("Không có quyền truy cập tin nhắn.");
        if (forMe)
        {
            if (await db.MessageDeletions.AnyAsync(d => d.MessageId == messageId && d.UserId == userId, ct)) return;
            db.MessageDeletions.Add(new MessageDeletion { MessageId = messageId, UserId = userId });
            await db.SaveChangesAsync(ct);
            member.UnreadCount = await UnreadAsync(member, ct);
        }
        else
        {
            var admin = GroupPermissionMatrix.HasPermission(member.Role, "DeleteOthersMessage");
            if (!admin && (message.SenderId != userId || message.CreatedAt.AddMinutes(15) < DateTime.UtcNow))
                throw AppException.Forbidden("Không có quyền thu hồi tin nhắn này.");
            if (message.DeletedAt != null) return;
            message.DeletedAt = DateTime.UtcNow;
            message.Status = MessageStatus.Deleted;
            await db.SaveChangesAsync(ct);
            var members = await db.ConversationMembers.Where(m => m.ConversationId == conversationId && m.LeftAt == null).ToListAsync(ct);
            foreach (var m in members) m.UnreadCount = await UnreadAsync(m, ct);
        }
        ConversationEvents.Add(db, conversationId, forMe ? "MessageHidden" : "MessageDeleted",
            new { ConversationId = conversationId, MessageId = messageId }, forMe ? userId : null);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<List<MessageStateResponse>> GetStatesAsync(Guid userId, Guid conversationId, Guid[] ids, CancellationToken ct = default)
    {
        if (ids.Length > 100) throw AppException.BadRequest("Tối đa 100 tin mỗi lần đồng bộ.");
        var member = await ReadableMemberAsync(userId, conversationId, ct);
        var query = MessageVisibility.Readable(db, userId, conversationId).Where(m => ids.Contains(m.Id));
        return await query.Select(m => new MessageStateResponse(m.Id, m.DeletedAt != null,
            db.MessageDeletions.Any(d => d.MessageId == m.Id && d.UserId == userId))).ToListAsync(ct);
    }

    private IQueryable<Message> VisibleMessages(ConversationMember member)
    {
        return MessageVisibility.Readable(db, member.UserId, member.ConversationId)
            .Where(m => !db.MessageDeletions.Any(d => d.MessageId == m.Id && d.UserId == member.UserId));
    }

    private Task<int> UnreadAsync(ConversationMember member, CancellationToken ct) =>
        VisibleMessages(member).CountAsync(m => m.Sequence > member.LastReadSequence && m.SenderId != member.UserId && m.DeletedAt == null, ct);

    private async Task<ConversationMember> ReadableMemberAsync(Guid userId, Guid id, CancellationToken ct) =>
        await db.ConversationMembers.SingleOrDefaultAsync(m => m.UserId == userId && m.ConversationId == id, ct)
        ?? throw AppException.Forbidden("Không có quyền truy cập hội thoại.");

    private async Task<ConversationMember> ActiveMemberAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var member = await ReadableMemberAsync(userId, id, ct);
        if (member.LeftAt != null || await db.Conversations.AnyAsync(c => c.Id == id && (c.ClosedAt != null || c.DeletedAt != null), ct)) throw AppException.Forbidden("Bạn đã rời hội thoại.");
        return member;
    }

    private static MessageResponse IdempotentResponse(Message message, Guid conversationId, string content)
    {
        if (message.ConversationId != conversationId || message.Content != content)
            throw AppException.Conflict("clientMessageId đã được dùng cho nội dung khác.");
        return ToResponse(message);
    }

    public static MessageResponse ToResponse(Message m) => new(m.Id, m.ConversationId, m.SenderId ?? Guid.Empty,
        m.Sequence, m.DeletedAt == null ? m.Content : "Tin nhắn đã được thu hồi", m.ClientMessageId,
        m.CreatedAt, m.EditedAt, m.DeletedAt == null ? "Sent" : "Deleted");
}
