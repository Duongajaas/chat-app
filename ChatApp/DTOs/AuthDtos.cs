using System.ComponentModel.DataAnnotations;

namespace ChatApp.DTOs;

public record RegisterRequest(
    [Required, MinLength(3), MaxLength(50)] string Username,
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    [Required, MaxLength(100)] string FullName
);

public record LoginRequest(
    [Required] string Username,
    [Required] string Password
);

public record GoogleLoginRequest(
    [Required] string IdToken
);

public record RefreshTokenRequest(
    [Required] string RefreshToken
);

public record LogoutRequest(
    [Required] string RefreshToken
);

public record ForgotPasswordRequest(
    [Required, EmailAddress] string Email
);

public record ResetPasswordRequest(
    [Required] string Token,
    [Required, MinLength(8)] string NewPassword
);

public record UserResponse(
    Guid Id,
    string Username,
    string? Email,
    string? Phone,
    string FullName,
    string? AvatarUrl,
    string? Bio,
    bool IsVerified
);

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    UserResponse User
);
