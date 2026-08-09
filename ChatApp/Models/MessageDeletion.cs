namespace ChatApp.Models;

// "Delete for me" (xóa phía riêng user, không ảnh hưởng người khác).
// "Delete for everyone" dùng Message.DeletedAt + Status = Deleted.
public class MessageDeletion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;
}
