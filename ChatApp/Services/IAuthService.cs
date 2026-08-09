using ChatApp.DTOs;

namespace ChatApp.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress, string? userAgent);
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent);
    Task<AuthResponse> GoogleLoginAsync(GoogleLoginRequest request, string? ipAddress, string? userAgent);
    Task<AuthResponse> RefreshTokenAsync(string rawRefreshToken, string? ipAddress, string? userAgent);
    Task LogoutAsync(string rawRefreshToken);
    Task ForgotPasswordAsync(ForgotPasswordRequest request);
    Task ResetPasswordAsync(ResetPasswordRequest request);
}
