using ChatApp.Common;
using ChatApp.Options;
using Google.Apis.Auth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatApp.Services;

public record GooglePayload(string GoogleId, string Email, string FullName, string? AvatarUrl, bool EmailVerified);

public interface IGoogleAuthService
{
    Task<GooglePayload> VerifyIdTokenAsync(string idToken);
}

public class GoogleAuthService : IGoogleAuthService
{
    private readonly GoogleAuthOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(
        IOptions<GoogleAuthOptions> options,
        IHostEnvironment environment,
        ILogger<GoogleAuthService> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<GooglePayload> VerifyIdTokenAsync(string idToken)
    {
        var clientId = _options.ClientId?.Trim();
        var token = idToken.Trim();

        if (string.IsNullOrWhiteSpace(clientId))
            throw AppException.BadRequest("Google ClientId chưa được cấu hình trên backend.");

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(token, settings);

            return new GooglePayload(
                GoogleId: payload.Subject,
                Email: payload.Email,
                FullName: payload.Name ?? payload.Email,
                AvatarUrl: payload.Picture,
                EmailVerified: payload.EmailVerified
            );
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Google ID token validation failed.");

            var message = _environment.IsDevelopment()
                ? $"Google ID token không hợp lệ hoặc đã hết hạn. Chi tiết: {ex.Message}"
                : "Google ID token không hợp lệ hoặc đã hết hạn.";

            throw AppException.Unauthorized(message);
        }
    }
}
