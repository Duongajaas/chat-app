namespace ChatApp.Models;

public enum AuthProvider
{
    Local,
    Google
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FullName { get; set; } = default!;
    public string Username { get; set; } = default!;
    public string? Email { get; set; }
    public string? Phone { get; set; }

    // Nullable vì tài khoản tạo qua Google có thể chưa có password
    public string? PasswordHash { get; set; }

    public AuthProvider AuthProvider { get; set; } = AuthProvider.Local;
    public string? GoogleId { get; set; }

    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsVerified { get; set; } = false;

    public DateTime? LastSeenAt { get; set; }
    public bool HideLastSeen { get; set; } = false;

    // Chống brute-force ở tầng application (kèm với rate limiter theo IP)
    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LockoutEnd { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
}
