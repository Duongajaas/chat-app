using ChatApp.DTOs;
using ChatApp.Models;

namespace ChatApp.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, string? ipAddress, string? userAgent);
    Task<AuthResult> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent);
    Task<AuthResult> GoogleLoginAsync(GoogleLoginRequest request, string? ipAddress, string? userAgent);
    Task<AuthResult> RefreshTokenAsync(string rawRefreshToken, string? ipAddress, string? userAgent);
    Task LogoutAsync(string rawRefreshToken);
    Task ForgotPasswordAsync(ForgotPasswordRequest request);
    Task ResetPasswordAsync(ResetPasswordRequest request);

    Task<List<DeviceResponse>> GetDevicesAsync(Guid userId);
    Task RevokeDeviceAsync(Guid userId, Guid deviceId);
    Task<User?> GetUserByIdAsync(Guid userId);
}
