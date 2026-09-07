namespace ChatApp.Models;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid? SenderId { get; set; }

    // client_message_id là UUID do client sinh; unique theo sender để chống retry lặp
    // dù cùng tin có thể được gửi vào 2 conversation khác nhau.
    public Guid ClientMessageId { get; set; }

    public long Sequence { get; set; }

    public MessageType Type { get; set; } = MessageType.Text;
    public string? Content { get; set; }

    public Guid? ReplyToMessageId { get; set; }
    public Guid? ForwardedFromMessageId { get; set; }

    public MessageStatus Status { get; set; } = MessageStatus.Sent;

    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
