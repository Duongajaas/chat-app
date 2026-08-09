namespace ChatApp.Models;

// Action ví dụ: RENAME_GROUP, KICK_MEMBER, ADD_MEMBER, CHANGE_AVATAR, TRANSFER_OWNERSHIP.
// Metadata lưu chi tiết (vd old_name/new_name).
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ConversationId { get; set; }
    public Guid? ActorId { get; set; }

    public string Action { get; set; } = default!;
    public Guid? TargetUserId { get; set; }

    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
