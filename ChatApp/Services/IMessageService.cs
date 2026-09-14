using ChatApp.DTOs;
namespace ChatApp.Services;
public interface IMessageService
{
    Task<MessageListResponse> GetMessagesAsync(Guid userId, Guid conversationId, long? before = null, long? after = null, int limit = 50, CancellationToken ct = default);
    Task<MessageResponse> SendMessageAsync(Guid userId, Guid clientMessageId, Guid conversationId, SendMessageRequest request, CancellationToken ct = default);
    Task MarkAsReadAsync(Guid userId, Guid conversationId, long sequence, CancellationToken ct = default);
    Task DeleteMessageAsync(Guid userId, Guid messageId, CancellationToken ct = default);
    Task DeleteForMeAsync(Guid userId, Guid messageId, CancellationToken ct = default);
    Task<List<MessageStateResponse>> GetStatesAsync(Guid userId, Guid conversationId, Guid[] ids, CancellationToken ct = default);
}
