namespace ChatApp.Models;

public class ConversationInvite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid? CreatedBy { get; set; }

    public string InviteCode { get; set; } = default!;
    public int? MaxUses { get; set; }
    public int UsedCount { get; set; } = 0;

    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
