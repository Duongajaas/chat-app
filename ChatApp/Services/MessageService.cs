using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Messages;
using ChatApp.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public class MessageService : IMessageService
{
    private readonly AppDbContext _db;
    private readonly IMediator _mediator;
    private readonly IConversationService _conversationService;
    private readonly IBlockService _blockService;

    public MessageService(
        AppDbContext db,
        IMediator mediator,
        IConversationService conversationService,
        IBlockService blockService)
    {
        _db = db;
        _mediator = mediator;
        _conversationService = conversationService;
        _blockService = blockService;
    }

    public async Task<MessageListResponse> GetMessagesAsync(Guid userId, Guid conversationId, long? before = null, long? after = null, int limit = 50)
    {
        var isMember = await _conversationService.IsMemberAsync(userId, conversationId);

        if (!isMember)
        {
            throw AppException.Forbidden("Bạn không có quyền xem cuộc trò chuyện này.");
        }

        // Validate: không cho phép cả before và after cùng lúc
        if (before.HasValue && after.HasValue)
        {
            throw AppException.BadRequest("Không được gửi cả 'before' lẫn 'after' cùng lúc.");
        }

        // Clamp limit
        var effectiveLimit = Math.Clamp(limit, 1, 100);

        var query = _db.Messages
            .Where(m => m.ConversationId == conversationId && m.DeletedAt == null)
            .AsQueryable();

        // Ba chế độ:
        if (after.HasValue)
        {
            // Reconnect Sync: lấy tin > after, sort cũ→mới
            query = query.Where(m => m.Sequence > after.Value)
                .OrderBy(m => m.Sequence);
        }
        else if (before.HasValue)
        {
            // Load Older: lấy tin < before, sort cũ→mới
            query = query.Where(m => m.Sequence < before.Value)
                .OrderByDescending(m => m.Sequence)
                .Take(effectiveLimit)
                .OrderBy(m => m.Sequence);
        }
        else
        {
            // Initial Load: N tin mới nhất, sort cũ→mới
            query = query.OrderByDescending(m => m.Sequence)
                .Take(effectiveLimit)
                .OrderBy(m => m.Sequence);
        }

        var messages = await query
            .Select(m => new MessageResponse(
                m.Id,
                m.ConversationId,
                m.SenderId ?? Guid.Empty,
                m.Sequence,
                m.Content,
                m.ClientMessageId,
                m.CreatedAt,
                m.EditedAt))
            .ToListAsync();

        // Tính NextCursor và HasMore
        long? nextCursor = null;
        bool hasMore = false;

        if (messages.Count > 0 && (before.HasValue || !after.HasValue))
        {
            // Có thể load older nếu đang ở chế độ Load Older hoặc Initial Load
            var oldestSequence = messages[0].Sequence;
            var countOlder = await _db.Messages
                .CountAsync(m => m.ConversationId == conversationId && m.Sequence < oldestSequence && m.DeletedAt == null);
            if (countOlder > 0)
            {
                nextCursor = oldestSequence;
                hasMore = true;
            }
        }

        return new MessageListResponse(messages, nextCursor, hasMore);
    }

    public async Task<MessageResponse> SendMessageAsync(Guid userId, Guid clientMessageId, Guid conversationId, SendMessageRequest request)
    {
        var isMember = await _conversationService.IsMemberAsync(userId, conversationId);

        if (!isMember)
        {
            throw AppException.Forbidden("Bạn không có quyền gửi tin nhắn trong cuộc trò chuyện này.");
        }

        var directPeerId = await _db.DirectConversations
            .Where(direct => direct.ConversationId == conversationId)
            .Select(direct => direct.UserLowId == userId ? direct.UserHighId : direct.UserLowId)
            .FirstOrDefaultAsync();

        if (directPeerId != Guid.Empty && await _blockService.IsBlockedEitherWayAsync(userId, directPeerId))
        {
            throw AppException.Forbidden("Không thể gửi tin nhắn.");
        }

        var existingMessage = await _db.Messages
            .FirstOrDefaultAsync(m => m.SenderId == userId && m.ClientMessageId == clientMessageId);

        if (existingMessage is not null)
        {
            return new MessageResponse(
                existingMessage.Id,
                existingMessage.ConversationId,
                existingMessage.SenderId ?? userId,
                existingMessage.Sequence,
                existingMessage.Content,
                existingMessage.ClientMessageId,
                existingMessage.CreatedAt,
                existingMessage.EditedAt);
        }

        var message = new Message
        {
            ConversationId = conversationId,
            SenderId = userId,
            ClientMessageId = clientMessageId,
            Content = request.Content,
            Type = MessageType.Text,
            CreatedAt = DateTime.UtcNow
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        await _db.ConversationMembers
            .Where(m => m.ConversationId == conversationId && m.UserId != userId && m.LeftAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.UnreadCount, m => m.UnreadCount + 1));

        var response = new MessageResponse(
            message.Id,
            message.ConversationId,
            message.SenderId ?? userId,
            message.Sequence,
            message.Content,
            message.ClientMessageId,
            message.CreatedAt,
            message.EditedAt);

        await _mediator.Publish(new MessageSentNotification(message.ConversationId, userId, response));

        return response;
    }

    public async Task DeleteMessageAsync(Guid userId, Guid messageId)
    {
        var message = await _db.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message is null)
        {
            throw AppException.NotFound("Tin nhắn không tồn tại.");
        }

        var isSender = message.SenderId == userId;
        var isMember = await _conversationService.IsMemberAsync(userId, message.ConversationId);

        if (!isSender && !isMember)
        {
            throw AppException.Forbidden("Bạn không có quyền xóa tin nhắn này.");
        }

        var callerRole = await _db.ConversationMembers
            .Where(x => x.ConversationId == message.ConversationId && x.UserId == userId)
            .Select(x => x.Role)
            .FirstOrDefaultAsync();

        var isAdminOrOwner = callerRole == MemberRole.Admin || callerRole == MemberRole.Owner;

        if (isSender)
        {
            var now = DateTime.UtcNow;
            var windowExceeded = message.CreatedAt.AddMinutes(15) < now;

            if (windowExceeded && !isAdminOrOwner)
            {
                throw AppException.Forbidden("Đã quá thời gian cho phép xóa với mọi người.");
            }
        }
        else if (!isAdminOrOwner)
        {
            throw AppException.Forbidden("Bạn không có quyền xóa tin nhắn của người khác.");
        }

        message.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
