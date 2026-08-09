namespace ChatApp.Models;

public class MessageDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public string? Content { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
