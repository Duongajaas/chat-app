namespace ChatApp.Models;

// Chỉ dùng cho tính năng "Seen by" (avatar ai đã xem trong group).
// Cursor đọc chính (unread count, badge) dùng ConversationMember.LastReadMessageId, KHÔNG dùng bảng này.
public class MessageRead
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}
