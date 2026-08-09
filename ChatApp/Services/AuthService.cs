using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatApp.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IGoogleAuthService _googleAuthService;
    private readonly IEmailService _emailService;
    private readonly AccountLockoutOptions _lockoutOptions;

    public AuthService(
        AppDbContext db,
        ITokenService tokenService,
        IGoogleAuthService googleAuthService,
        IEmailService emailService,
        IOptions<AccountLockoutOptions> lockoutOptions)
    {
        _db = db;
        _tokenService = tokenService;
        _googleAuthService = googleAuthService;
        _emailService = emailService;
        _lockoutOptions = lockoutOptions.Value;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress, string? userAgent)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        var email = request.Email.Trim().ToLowerInvariant();

        var exists = await _db.Users.AnyAsync(u => u.Username == username || u.Email == email);
        if (exists)
            throw AppException.Conflict("Username hoặc email đã được sử dụng.");

        var user = new User
        {
            Username = username,
            Email = email,
            FullName = request.FullName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            AuthProvider = AuthProvider.Local
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return await IssueTokensAsync(user, ipAddress, userAgent);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username && u.DeletedAt == null);

        // Không tiết lộ "user không tồn tại" hay "sai mật khẩu" — tránh user enumeration
        const string invalidCredentialsMessage = "Username hoặc mật khẩu không đúng.";

        if (user is null)
            throw AppException.Unauthorized(invalidCredentialsMessage);

        if (!user.IsActive)
            throw AppException.Forbidden("Tài khoản đã bị vô hiệu hóa.");

        if (user.LockoutEnd is not null && user.LockoutEnd > DateTime.UtcNow)
        {
            var remaining = (int)Math.Ceiling((user.LockoutEnd.Value - DateTime.UtcNow).TotalMinutes);
            throw AppException.TooManyRequests(
                $"Tài khoản tạm thời bị khóa do đăng nhập sai nhiều lần. Thử lại sau {remaining} phút.");
        }

        if (user.PasswordHash is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;

            if (user.FailedLoginAttempts >= _lockoutOptions.MaxFailedAttempts)
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(_lockoutOptions.LockoutMinutes);
                user.FailedLoginAttempts = 0;
            }

            await _db.SaveChangesAsync();
            throw AppException.Unauthorized(invalidCredentialsMessage);
        }

        // Đăng nhập thành công -> reset bộ đếm
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await IssueTokensAsync(user, ipAddress, userAgent);
    }

    public async Task<AuthResponse> GoogleLoginAsync(GoogleLoginRequest request, string? ipAddress, string? userAgent)
    {
        var payload = await _googleAuthService.VerifyIdTokenAsync(request.IdToken);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.GoogleId == payload.GoogleId);

        if (user is null)
        {
            // Đã có tài khoản local dùng chung email -> link Google vào tài khoản đó
            user = await _db.Users.FirstOrDefaultAsync(u => u.Email == payload.Email.ToLowerInvariant());

            if (user is not null)
            {
                user.GoogleId = payload.GoogleId;
            }
            else
            {
                user = new User
                {
                    Username = await GenerateUniqueUsernameAsync(payload.Email),
                    Email = payload.Email.ToLowerInvariant(),
                    FullName = payload.FullName,
                    AvatarUrl = payload.AvatarUrl,
                    GoogleId = payload.GoogleId,
                    AuthProvider = AuthProvider.Google,
                    IsVerified = payload.EmailVerified,
                    PasswordHash = null
                };
                _db.Users.Add(user);
            }
        }

        if (!user.IsActive)
            throw AppException.Forbidden("Tài khoản đã bị vô hiệu hóa.");

        user.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await IssueTokensAsync(user, ipAddress, userAgent);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string rawRefreshToken, string? ipAddress, string? userAgent)
    {
        var tokenHash = _tokenService.HashToken(rawRefreshToken);

        var existingToken = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (existingToken is null || !existingToken.IsActive)
            throw AppException.Unauthorized("Refresh token không hợp lệ hoặc đã hết hạn.");

        if (!existingToken.User.IsActive)
            throw AppException.Forbidden("Tài khoản đã bị vô hiệu hóa.");

        // Rotate: thu hồi token cũ, phát hành token mới — giảm rủi ro nếu token bị lộ
        existingToken.RevokedAt = DateTime.UtcNow;

        var response = await IssueTokensAsync(existingToken.User, ipAddress, userAgent);
        await _db.SaveChangesAsync();

        return response;
    }

    public async Task LogoutAsync(string rawRefreshToken)
    {
        var tokenHash = _tokenService.HashToken(rawRefreshToken);

        // Chỉ thu hồi refresh token của THIẾT BỊ HIỆN TẠI, không đụng tới các thiết bị khác
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);
        if (token is not null && token.RevokedAt is null)
        {
            token.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.DeletedAt == null);

        // Luôn trả về thành công dù email có tồn tại hay không — tránh lộ thông tin user nào đang dùng hệ thống
        if (user is null) return;

        var rawToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var resetToken = new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = _tokenService.HashToken(rawToken),
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        _db.PasswordResetTokens.Add(resetToken);
        await _db.SaveChangesAsync();

        await _emailService.SendPasswordResetEmailAsync(user.Email!, rawToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var tokenHash = _tokenService.HashToken(request.Token);

        var resetToken = await _db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (resetToken is null || !resetToken.IsValid)
            throw AppException.BadRequest("Token reset password không hợp lệ hoặc đã hết hạn.");

        resetToken.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        resetToken.User.FailedLoginAttempts = 0;
        resetToken.User.LockoutEnd = null;
        resetToken.UsedAt = DateTime.UtcNow;

        // Business rule: đổi password -> logout tất cả thiết bị (thu hồi toàn bộ refresh token)
        var activeTokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == resetToken.UserId && rt.RevokedAt == null)
            .ToListAsync();

        foreach (var rt in activeTokens)
            rt.RevokedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, string? ipAddress, string? userAgent)
    {
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshToken.TokenHash,
            ExpiresAt = refreshToken.ExpiresAt,
            IpAddress = ipAddress,
            UserAgent = userAgent
        });

        await _db.SaveChangesAsync();

        return new AuthResponse(
            AccessToken: accessToken.Token,
            RefreshToken: refreshToken.RawToken,
            AccessTokenExpiresAt: accessToken.ExpiresAt,
            User: new UserResponse(user.Id, user.Username, user.Email, user.Phone, user.FullName, user.AvatarUrl, user.Bio, user.IsVerified)
        );
    }

    private async Task<string> GenerateUniqueUsernameAsync(string email)
    {
        var baseUsername = email.Split('@')[0].ToLowerInvariant();
        var candidate = baseUsername;
        var suffix = 0;

        while (await _db.Users.AnyAsync(u => u.Username == candidate))
        {
            suffix++;
            candidate = $"{baseUsername}{suffix}";
        }

        return candidate;
    }
}
