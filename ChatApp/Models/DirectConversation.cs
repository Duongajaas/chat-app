namespace ChatApp.Models;

public class DirectConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid UserLowId { get; set; }
    public Guid UserHighId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
