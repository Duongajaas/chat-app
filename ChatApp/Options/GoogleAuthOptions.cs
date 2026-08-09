namespace ChatApp.Options;

public class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    public string ClientId { get; set; } = default!;
}

public class AccountLockoutOptions
{
    public const string SectionName = "AccountLockout";

    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public class RateLimitRule
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitRule Login { get; set; } = new();
    public RateLimitRule Register { get; set; } = new();
    public RateLimitRule ForgotPassword { get; set; } = new();
    public RateLimitRule GoogleLogin { get; set; } = new();
}
