namespace ChatApp.Models;

public class ScheduledMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid SenderId { get; set; }

    public MessageType Type { get; set; } = MessageType.Text;
    public string? Content { get; set; }

    public DateTime ScheduledAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? CanceledAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
