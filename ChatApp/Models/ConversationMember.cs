namespace ChatApp.Models;

public class ConversationMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }

    public MemberRole Role { get; set; } = MemberRole.Member;
    public string? Nickname { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }

    public DateTime? MutedUntil { get; set; }

    public Guid? LastReadMessageId { get; set; }
    public DateTime? LastReadAt { get; set; }
    public int UnreadCount { get; set; } = 0;

    public bool IsPinned { get; set; } = false;
    public DateTime? ArchivedAt { get; set; }

    // hidden_at: user "delete conversation" phía client — chỉ ẩn, không xóa DB.
    public DateTime? HiddenAt { get; set; }
}
