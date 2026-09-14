using System.ComponentModel.DataAnnotations;
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
    Guid? PeerUserId = null,
    string? LastMessage = null,
    bool IsAdmin = false,
    bool IsHidden = false,
    bool HasLeft = false,
    MemberRole? Role = null,
    string[]? Permissions = null,
    int MemberCount = 0,
    long Version = 0,
    DateTime? ClosedAt = null
);

public record MessageResponse(
    Guid Id,
    Guid ConversationId,
    Guid SenderId,
    long Sequence,
    string? Content,
    Guid? ClientMessageId,
    DateTime CreatedAt,
    DateTime? EditedAt = null,
    string Status = "Sent"
);

public record MessageListResponse(
    List<MessageResponse> Messages,
    long? NextCursor,
    bool HasMore,
    long PeerReadSequence = 0
);

public record CreateConversationRequest(
    [EnumDataType(typeof(ConversationType))] ConversationType Type,
    [Required, StringLength(100)] string? Name,
    [Required, MinLength(1), MaxLength(99)] Guid[] MemberIds
);

public record SendMessageRequest(
    string? Content
);

public record SendMessagePayload(
    [Required] Guid? ClientMessageId,
    [Required, StringLength(4000)] string? Content
);

public record MessageStateResponse(Guid Id, bool Deleted, bool Hidden);
public record MessageStatesRequest([Required, MaxLength(100)] Guid[] Ids);
public record ConversationPageResponse(List<ConversationSummaryResponse> Items, string? NextCursor);
