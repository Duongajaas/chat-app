namespace ChatApp.Models;

public class CallParticipant
{
    public Guid? EndpointId { get; set; }
    public Guid? AuthSessionId { get; set; }
    public string? ConnectionId { get; set; }
    public string? CredentialHash { get; set; }
    public long EndpointGeneration { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime? LastHeartbeatAt { get; set; }
    public DateTime? MediaConnectedAt { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CallSessionId { get; set; }
    public Guid? UserId { get; set; }

    public CallParticipantStatus Status { get; set; } = CallParticipantStatus.Invited;

    public DateTime? JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
