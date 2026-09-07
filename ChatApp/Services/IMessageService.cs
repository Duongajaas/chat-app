using ChatApp.DTOs;

namespace ChatApp.Services;

public interface IMessageService
{
    /// <summary>
    /// Lấy tin nhắn với 3 chế độ:
    /// - Initial: before=null, after=null → N tin mới nhất
    /// - Load Older: before=X → tin có sequence < X
    /// - Reconnect Sync: after=Y → tin có sequence > Y
    /// Không cho phép truyền cả before lẫn after cùng lúc.
    /// </summary>
    Task<MessageListResponse> GetMessagesAsync(Guid userId, Guid conversationId, long? before = null, long? after = null, int limit = 50);
    Task<MessageResponse> SendMessageAsync(Guid userId, Guid clientMessageId, Guid conversationId, SendMessageRequest request);
    Task DeleteMessageAsync(Guid userId, Guid messageId);
}