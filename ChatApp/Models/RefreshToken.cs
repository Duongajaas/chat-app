namespace ChatApp.Models;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    // Không bao giờ lưu raw token, chỉ lưu hash (SHA-256)
    public string TokenHash { get; set; } = default!;

    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive => RevokedAt == null && ExpiresAt > DateTime.UtcNow;
}
