namespace ChatApp.Models;

public class PinnedMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
    public Guid? PinnedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
