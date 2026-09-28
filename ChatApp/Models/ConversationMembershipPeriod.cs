namespace ChatApp.Models;

// (StartSequence, EndSequence] is readable. Null end means the current membership.
public class ConversationMembershipPeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public long StartSequence { get; set; }
    public long? EndSequence { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }
}
