using System.ComponentModel.DataAnnotations;

namespace ChatApp.DTOs;

public record ReconcileItem(Guid ClientMessageId, [StringLength(4000)] string? Content = null,
    Guid? ReplyToMessageId = null, [MaxLength(20)] MentionInput[]? Mentions = null, Guid? SourceMessageId = null,
    string? MediaUrl = null, int? VoiceDuration = null, string? WaveformPoints = null);
public record ReconcileRequest([Required, MinLength(1), MaxLength(100)] ReconcileItem[] Items);
public record MessageReceipt(Guid ClientMessageId, string State, Guid? MessageId = null, MessageResponse? Message = null);
