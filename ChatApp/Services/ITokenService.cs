using ChatApp.Models;

namespace ChatApp.Services;

public record GeneratedAccessToken(string Token, DateTime ExpiresAt);
public record GeneratedRefreshToken(string RawToken, string TokenHash, DateTime ExpiresAt);

public interface ITokenService
{
    GeneratedAccessToken GenerateAccessToken(User user);
    GeneratedRefreshToken GenerateRefreshToken();
    string HashToken(string rawToken);
}
