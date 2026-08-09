namespace ChatApp.Models;

public class CallSession
{
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
