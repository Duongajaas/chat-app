using System.ComponentModel.DataAnnotations;

namespace ChatApp.DTOs;

// Offsets are UTF-16 code units, matching JavaScript string indexes.
public record MentionInput(Guid UserId, int Start, int Length);
public record MentionResponse(Guid UserId, string Username, int Start, int Length);
public record ReplyPreviewResponse(Guid Id, string? SenderName, string? ContentSnippet, bool IsAvailable, long? Sequence = null);
public record AttachmentResponse(Guid Id, string? FileName, string? FileType, long? FileSize);
public record ForwardMessageRequest(Guid TargetConversationId, Guid ClientMessageId);
public record PinnedMessageResponse(Guid Id, Guid? PinnedBy, DateTime CreatedAt, MessageResponse Message);
public record MessageBatchRequest([Required, MaxLength(100)] Guid[] Ids);
