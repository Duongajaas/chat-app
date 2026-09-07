using ChatApp.Models;

namespace ChatApp.DTOs;

public record ConversationSummaryResponse(
    Guid Id,
    string? Name,
    ConversationType Type,
    int UnreadCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? LastMessageId,
    DateTime? LastMessageAt,
    bool IsBlocked = false,
    Guid? PeerUserId = null
);

public record MessageResponse(
    Guid Id,
    Guid ConversationId,
    Guid SenderId,
    long Sequence,
    string? Content,
    Guid? ClientMessageId,
    DateTime CreatedAt,
    DateTime? EditedAt = null
);

public record MessageListResponse(
    List<MessageResponse> Messages,
    long? NextCursor,
    bool HasMore
);

public record CreateConversationRequest(
    ConversationType Type,
    string? Name,
    Guid[] MemberIds
);

public record SendMessageRequest(
    string? Content
);

public record SendMessagePayload(
    Guid? ClientMessageId,
    string? Content
);
