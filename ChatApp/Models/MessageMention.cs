namespace ChatApp.Models;

public class MessageMention
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public Guid MentionedUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
