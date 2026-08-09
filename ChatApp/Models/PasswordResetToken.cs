namespace ChatApp.Models;

public class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    // Hash của token gửi qua email, không lưu raw token trong DB
    public string TokenHash { get; set; } = default!;

    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsValid => UsedAt == null && ExpiresAt > DateTime.UtcNow;
}
