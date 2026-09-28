using System.Text.Json;
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
        return new MessageListResponse(await new MessageReader(db).BuildAsync(userId, conversationId, page, ct), cursor, more, peerRead);
    }

    public Task<MessageResponse> SendMessageAsync(Guid userId, Guid clientMessageId, Guid conversationId,
        SendMessageRequest request, CancellationToken ct = default) =>
        CreateAsync(userId, clientMessageId, conversationId, request, null, ct);

    public Task<MessageResponse> ForwardAsync(Guid userId, Guid sourceId, Guid targetId, Guid clientMessageId, CancellationToken ct = default) =>
        CreateAsync(userId, clientMessageId, targetId, new(null), sourceId, ct);

    private async Task<MessageResponse> CreateAsync(Guid userId, Guid clientMessageId, Guid conversationId,
        SendMessageRequest request, Guid? sourceId, CancellationToken ct)
    {
        if (clientMessageId == Guid.Empty) throw AppException.BadRequest("clientMessageId là bắt buộc.");
        if (request.Mentions?.Length > 20) throw AppException.BadRequest("Tối đa 20 vị trí mention.");
        var requestHash = MessageReconciliation.Hash(conversationId, request, sourceId);
        var sourceConversation = sourceId == null ? null : await db.Messages.Where(m => m.Id == sourceId)
            .Select(m => (Guid?)m.ConversationId).SingleOrDefaultAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Two-conversation operations always acquire locks in the same order.
        foreach (var id in new[] { conversationId, sourceConversation ?? conversationId }.Distinct().Order())
            await ConversationLock.AcquireAsync(db, id, ct);
        var existing = await db.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.SenderId == userId && m.ClientMessageId == clientMessageId, ct);
        if (existing != null)
        {
            CheckRetry(existing, conversationId, requestHash, request.Content, sourceId);
            return await new MessageReconciliation(db).ExistingResponseAsync(userId, existing, ct);
        }
        await ActiveMemberAsync(userId, conversationId, ct);
        var direct = await db.DirectConversations.SingleOrDefaultAsync(d => d.ConversationId == conversationId, ct);
        if (direct != null && await blocks.IsBlockedEitherWayAsync(direct.UserLowId, direct.UserHighId))
            throw AppException.Forbidden("Không thể gửi tin nhắn.");
        Message? source = null;
        var content = request.Content ?? "";
        if (sourceId != null)
        {
            if (sourceConversation == null) throw AppException.NotFound("Tin nhắn nguồn không khả dụng.");
            await new MessageReader(db).EnsureMembershipAsync(userId, sourceConversation.Value, ct);
            source = await new MessageReader(db).Visible(userId, sourceConversation.Value)
                .SingleOrDefaultAsync(m => m.Id == sourceId && m.DeletedAt == null, ct)
                ?? throw AppException.NotFound("Tin nhắn nguồn không khả dụng.");
            var sourceDirect = await db.DirectConversations.SingleOrDefaultAsync(d => d.ConversationId == sourceConversation, ct);
            if (sourceDirect != null && await blocks.IsBlockedEitherWayAsync(sourceDirect.UserLowId, sourceDirect.UserHighId))
                throw AppException.Forbidden("Không thể chuyển tiếp từ hội thoại bị chặn.");
            content = source.Content ?? "";
        }
        else if (string.IsNullOrWhiteSpace(content) || content.Length > 4000)
            throw AppException.BadRequest("Tin nhắn phải có từ 1 đến 4000 ký tự.");
        if (request.ReplyToMessageId is Guid replyId &&
            !await new MessageReader(db).Visible(userId, conversationId).AnyAsync(m => m.Id == replyId && m.DeletedAt == null, ct))
            throw AppException.BadRequest("Tin nhắn được trả lời không khả dụng trong hội thoại.");
        var message = new Message { ConversationId = conversationId, SenderId = userId, ClientMessageId = clientMessageId,
            Content = content, ReplyToMessageId = request.ReplyToMessageId, ForwardedFromMessageId = source?.Id,
            IsForwarded = source != null, RequestHash = requestHash, Type = source?.Type ?? MessageType.Text };
        db.Messages.Add(message);
        if (source == null) await AddMentionsAsync(message, request.Mentions ?? [], ct);
        if (source != null)
        {
            var attachments = await db.MessageAttachments.AsNoTracking().Where(a => a.MessageId == source.Id).ToListAsync(ct);
            db.MessageAttachments.AddRange(attachments.Select(a => new MessageAttachment { MessageId = message.Id,
                StorageKey = a.StorageKey, FileName = a.FileName, FileType = a.FileType, FileSize = a.FileSize,
                Width = a.Width, Height = a.Height, DurationSeconds = a.DurationSeconds, ThumbnailKey = a.ThumbnailKey, Metadata = a.Metadata }));
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_messages_sender_id_client_message_id" })
        {
            await tx.RollbackAsync(ct); db.ChangeTracker.Clear();
            var winner = await db.Messages.AsNoTracking().SingleAsync(m => m.SenderId == userId && m.ClientMessageId == clientMessageId, ct);
            CheckRetry(winner, conversationId, requestHash, request.Content, sourceId);
            return await new MessageReconciliation(db).ExistingResponseAsync(userId, winner, ct);
        }
        await db.ConversationMembers.Where(m => m.ConversationId == conversationId && m.LeftAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.HiddenAt, (DateTime?)null)
                .SetProperty(m => m.UnreadCount, m => m.UnreadCount + (m.UserId == userId ? 0 : 1)), ct);
        await db.Conversations.Where(c => c.Id == conversationId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.LastMessageId, (Guid?)message.Id)
            .SetProperty(c => c.LastMessageAt, (DateTime?)message.CreatedAt)
            .SetProperty(c => c.UpdatedAt, message.CreatedAt), ct);
        // Plain messages retain the compatible event; richer messages contain no shared quote/attachment payload.
        if (message.ReplyToMessageId != null || message.IsForwarded || (request.Mentions?.Length ?? 0) > 0)
            ConversationEvents.Add(db, conversationId, "MessageAvailable", new { ConversationId = conversationId, MessageId = message.Id, message.Sequence });
        else ConversationEvents.Add(db, conversationId, "ReceiveMessage", ToResponse(message));
        var mentions = await db.MessageMentions.Where(m => m.MessageId == message.Id).ToListAsync(ct);
        foreach (var mention in mentions)
        {
            db.Notifications.Add(new Notification { UserId = mention.MentionedUserId, Type = "mention",
                Title = "Bạn được nhắc đến", Data = JsonSerializer.Serialize(new { conversationId, messageId = message.Id }) });
            ConversationEvents.Add(db, conversationId, "MentionReceived",
                new { ConversationId = conversationId, MessageId = message.Id, message.Sequence }, mention.MentionedUserId);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await new MessageReconciliation(db).ExistingResponseAsync(userId, message, ct);
    }

    private async Task AddMentionsAsync(Message message, MentionInput[] inputs, CancellationToken ct)
    {
        if (inputs.Length == 0) return;
        if (!await db.Conversations.AnyAsync(c => c.Id == message.ConversationId && c.Type == ConversationType.Group, ct))
            throw AppException.BadRequest("Mention chỉ áp dụng trong nhóm.");
        var ids = inputs.Select(i => i.UserId).Distinct().ToArray();
        var users = await (from m in db.ConversationMembers join u in db.Users on m.UserId equals u.Id
            where m.ConversationId == message.ConversationId && m.LeftAt == null && ids.Contains(u.Id) && u.IsActive && u.DeletedAt == null
            select new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, ct);
        var seen = new HashSet<Guid>(); var end = 0;
        foreach (var input in inputs.OrderBy(i => i.Start))
        {
            if (input.Start < end || input.Start < 0 || input.Length < 2 ||
                input.Start > message.Content!.Length - input.Length)
                throw AppException.BadRequest("Vị trí mention không hợp lệ.");
            end = input.Start + input.Length;
            if (input.UserId == message.SenderId || !users.TryGetValue(input.UserId, out var target)) continue;
            if (!string.Equals(message.Content.Substring(input.Start, input.Length), "@" + target.Username, StringComparison.OrdinalIgnoreCase))
                throw AppException.BadRequest("Mention không khớp username.");
            if (!seen.Add(input.UserId)) continue;
            if (seen.Count > 10) throw AppException.BadRequest("Tối đa 10 người được mention trong một tin.");
            db.MessageMentions.Add(new MessageMention { MessageId = message.Id, MentionedUserId = input.UserId,
                Username = target.Username, Start = input.Start, Length = input.Length });
        }
    }

    private static void CheckRetry(Message message, Guid conversation, string hash, string? content, Guid? source)
    {
        if (message.ConversationId != conversation || (message.RequestHash != null ? message.RequestHash != hash :
            source != null || message.Content != content?.Trim()))
            throw AppException.Conflict("clientMessageId đã dùng cho yêu cầu khác.");
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
            var pinsRemoved = await db.PinnedMessages.Where(p => p.MessageId == messageId).ExecuteDeleteAsync(ct);
            if (pinsRemoved > 0)
            {
                var conversation = await db.Conversations.SingleAsync(c => c.Id == conversationId, ct);
                new GroupService(db).Changed(conversation, userId, "UNPIN_RECALLED_MESSAGE", metadata: new { messageId });
                ConversationEvents.Add(db, conversationId, "PinsChanged", new { ConversationId = conversationId, conversation.Version });
            }
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

    public static MessageResponse ToResponse(Message m) => new(m.Id, m.ConversationId, m.SenderId ?? Guid.Empty,
        m.Sequence, m.DeletedAt == null ? m.Content : "Tin nhắn đã được thu hồi", m.ClientMessageId,
        m.CreatedAt, m.EditedAt, m.DeletedAt == null ? "Sent" : "Deleted");
}
