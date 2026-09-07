namespace ChatApp.Models;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid FamilyId { get; set; } = Guid.NewGuid();
    public User User { get; set; } = default!;
    public UserDevice? Device { get; set; }

    // Không bao giờ lưu raw token, chỉ lưu hash (SHA-256)
    public string TokenHash { get; set; } = default!;

    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string? RevocationReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive => UsedAt == null && RevokedAt == null && ExpiresAt > DateTime.UtcNow;
}
