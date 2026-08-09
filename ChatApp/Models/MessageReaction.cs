namespace ChatApp.Models;

public class MessageReaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public string Emoji { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
