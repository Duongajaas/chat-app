using ChatApp.DTOs;

namespace ChatApp.Services;

public interface IConversationService
{
    Task<ConversationSummaryResponse> GetOrCreateDirectConversationAsync(Guid userId, Guid otherUserId);
    Task<bool> IsMemberAsync(Guid userId, Guid conversationId);
    Task<ConversationPageResponse> GetMyConversationsAsync(Guid userId, string? cursor = null, int limit = 30);
    Task LeaveConversationAsync(Guid userId, Guid conversationId);
    Task<ConversationSummaryResponse> GetConversationByIdAsync(Guid userId, Guid conversationId);
    Task<ConversationSummaryResponse> CreateConversationAsync(Guid userId, CreateConversationRequest request, Guid? key = null);
    Task DeleteConversationAsync(Guid userId, Guid conversationId);
}