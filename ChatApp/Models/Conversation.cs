namespace ChatApp.Models;

public class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ConversationType Type { get; set; }
    public string? Name { get; set; }
    public string? AvatarUrl { get; set; }
    public Guid? CreatedBy { get; set; }

    // Circular FK có chủ đích (query last message nhanh) — tạo conversation trước (null),
    // update lại sau khi có message đầu tiên. Xem note trong AppDbContext.
    public Guid? LastMessageId { get; set; }
    public DateTime? LastMessageAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}
