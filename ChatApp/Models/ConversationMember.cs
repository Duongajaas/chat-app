namespace ChatApp.Models;

public class ConversationMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }

    public MemberRole Role { get; set; } = MemberRole.Member;
    public MemberRequestStatus RequestStatus { get; set; } = MemberRequestStatus.Accepted;
    public string? Nickname { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public long? LeftAtSequence { get; set; }
    public long LastReadSequence { get; set; }
    public DateTime? LeftAt { get; set; }
    public string? LeaveReason { get; set; }
    public Guid? RemovedByUserId { get; set; }

    public DateTime? MutedUntil { get; set; }

    public Guid? LastReadMessageId { get; set; }
    public DateTime? LastReadAt { get; set; }
    public int UnreadCount { get; set; } = 0;

    public bool IsPinned { get; set; } = false;
    public DateTime? ArchivedAt { get; set; }

    // hidden_at: user "delete conversation" phía client — chỉ ẩn, không xóa DB.
    public DateTime? HiddenAt { get; set; }
}
