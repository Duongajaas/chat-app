using ChatApp.Common;
using ChatApp.Options;
using Google.Apis.Auth;
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

    public GoogleAuthService(IOptions<GoogleAuthOptions> options)
    {
        _options = options.Value;
    }

    public async Task<GooglePayload> VerifyIdTokenAsync(string idToken)
    {
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _options.ClientId }
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            return new GooglePayload(
                GoogleId: payload.Subject,
                Email: payload.Email,
                FullName: payload.Name ?? payload.Email,
                AvatarUrl: payload.Picture,
                EmailVerified: payload.EmailVerified
            );
        }
        catch (InvalidJwtException)
        {
            throw AppException.Unauthorized("Google ID token không hợp lệ hoặc đã hết hạn.");
        }
    }
}
