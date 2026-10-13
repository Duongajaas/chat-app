namespace ChatApp.Models;

public class CallSession
{
    public Guid? ClientCallId { get; set; }
    public string? RequestHash { get; set; }
    public long Version { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ConnectDeadlineAt { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public string? EndedReason { get; set; }
    public int NegotiationEpoch { get; set; }
    public int RestartCount { get; set; }
    public DateTime? RecoveryDeadlineAt { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ConversationId { get; set; }
    public Guid? CallerId { get; set; }

    public CallType Type { get; set; }
    public CallStatus Status { get; set; } = CallStatus.Ringing;

    public string Provider { get; set; } = "WEBRTC";
    public string? ProviderRoomId { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public int? DurationSeconds { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
