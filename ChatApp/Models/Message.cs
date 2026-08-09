namespace ChatApp.Models;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid? SenderId { get; set; }

    // Do client sinh (UUID) khi gửi; unique theo conversation để chống insert trùng
    // lúc client retry do mất mạng/timeout.
    public Guid ClientMessageId { get; set; }

    public MessageType Type { get; set; } = MessageType.Text;
    public string? Content { get; set; }

    public Guid? ReplyToMessageId { get; set; }
    public Guid? ForwardedFromMessageId { get; set; }

    public MessageStatus Status { get; set; } = MessageStatus.Sent;

    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
