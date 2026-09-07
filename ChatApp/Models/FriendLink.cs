namespace ChatApp.Models;

public class FriendLink
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    // TokenHash is used for validation and lookup. Token is retained only so the owner can display the link again.
    public string TokenHash { get; set; } = default!;
    public string Token { get; set; } = default!;

    public DateTime? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int UsedCount { get; set; } = 0;
    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive =>
        RevokedAt == null &&
        (ExpiresAt == null || ExpiresAt > DateTime.UtcNow) &&
        (MaxUses == null || UsedCount < MaxUses);
}