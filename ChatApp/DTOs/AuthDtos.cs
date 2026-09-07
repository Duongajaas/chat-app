using System.ComponentModel.DataAnnotations;
using ChatApp.Models;

namespace ChatApp.DTOs;

public record RegisterRequest(
    [Required, MinLength(3), MaxLength(50)] string Username,
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    [Required, MaxLength(100)] string FullName,
    DeviceInfo? Device = null
);

public record DeviceInfo(
    [Required] string DeviceToken,
    string? DeviceName,
    DevicePlatform Platform
);

public record LoginRequest(
    [Required] string Username,
    [Required] string Password,
    DeviceInfo? Device = null
);

public record GoogleLoginRequest(
    [Required] string IdToken,
    DeviceInfo? Device = null
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
    DateTime AccessTokenExpiresAt,
    UserResponse User
);

public record AuthResult(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserResponse User
);

public record DeviceResponse(
    Guid Id,
    string? DeviceName,
    DevicePlatform Platform,
    DateTime? LastActiveAt
);
