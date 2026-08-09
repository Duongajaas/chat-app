namespace ChatApp.Models;

public class CallParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CallSessionId { get; set; }
    public Guid? UserId { get; set; }

    public CallParticipantStatus Status { get; set; } = CallParticipantStatus.Invited;

    public DateTime? JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
