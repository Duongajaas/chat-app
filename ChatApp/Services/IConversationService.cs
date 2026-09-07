using ChatApp.DTOs;

namespace ChatApp.Services;

public interface IConversationService
{
    Task<ConversationSummaryResponse> GetOrCreateDirectConversationAsync(Guid userId, Guid otherUserId);
    Task<bool> IsMemberAsync(Guid userId, Guid conversationId);
    Task<List<ConversationSummaryResponse>> GetMyConversationsAsync(Guid userId);
    Task<ConversationSummaryResponse> GetConversationByIdAsync(Guid userId, Guid conversationId);
    Task<ConversationSummaryResponse> CreateConversationAsync(Guid userId, CreateConversationRequest request);
    Task DeleteConversationAsync(Guid userId, Guid conversationId);
}