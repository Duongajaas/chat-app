using System.Security.Claims;
using ChatApp.Common;
using ChatApp.DTOs;
using ChatApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const string RefreshCookieName = "__Secure-refreshToken";
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;

    public AuthController(IAuthService authService, IConfiguration configuration)
    {
        _authService = authService;
        _configuration = configuration;
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? UserAgent => Request.Headers.UserAgent.ToString();

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Register)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        ValidateTrustedOrigin();
        var result = await _authService.RegisterAsync(request, ClientIp, UserAgent);
        return AuthOk(result);
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        ValidateTrustedOrigin();
        var result = await _authService.LoginAsync(request, ClientIp, UserAgent);
        return AuthOk(result);
    }

    [HttpPost("google-login")]
    [EnableRateLimiting(RateLimitPolicies.GoogleLogin)]
    public async Task<ActionResult<AuthResponse>> GoogleLogin(GoogleLoginRequest request)
    {
        ValidateTrustedOrigin();
        var result = await _authService.GoogleLoginAsync(request, ClientIp, UserAgent);
        return AuthOk(result);
    }

    [HttpPost("refresh-token")]
    [EnableRateLimiting(RateLimitPolicies.RefreshToken)]
    public async Task<ActionResult<AuthResponse>> RefreshToken()
    {
        ValidateTrustedOrigin();

        var rawRefreshToken = Request.Cookies[RefreshCookieName];
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return Unauthorized();

        try
        {
            var result = await _authService.RefreshTokenAsync(rawRefreshToken, ClientIp, UserAgent);
            return AuthOk(result);
        }
        catch (AppException ex) when (ex.StatusCode == StatusCodes.Status401Unauthorized)
        {
            DeleteRefreshTokenCookie();
            throw;
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        ValidateTrustedOrigin();

        var rawRefreshToken = Request.Cookies[RefreshCookieName];
        if (!string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            await _authService.LogoutAsync(rawRefreshToken);
        }

        DeleteRefreshTokenCookie();
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.ForgotPassword)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        ValidateTrustedOrigin();
        await _authService.ForgotPasswordAsync(request);
        // Luôn trả về thông báo chung chung, không tiết lộ email có tồn tại hay không
        return Ok(new { message = "Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi." });
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitPolicies.ResetPassword)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        ValidateTrustedOrigin();
        await _authService.ResetPasswordAsync(request);
        return Ok(new { message = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại trên tất cả thiết bị." });
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var username = User.FindFirstValue("username");
        return Ok(new { userId, username });
    }

    [HttpGet("devices")]
    [Authorize]
    public async Task<IActionResult> GetDevices()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        Guid userGuid = Guid.Parse(userId);

        var devices = await _authService.GetDevicesAsync(userGuid);
        return Ok(devices);
    }

    [HttpDelete("devices/{deviceId}")]
    [Authorize]
    public async Task<IActionResult> DeleteDevice(string deviceId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        Guid userGuid = Guid.Parse(userId);
        Guid deviceGuid = Guid.Parse(deviceId);
        await _authService.RevokeDeviceAsync(userGuid, deviceGuid);
        return NoContent();
    }

    private ActionResult<AuthResponse> AuthOk(AuthResult result)
    {
        SetRefreshTokenCookie(result.RefreshToken, result.RefreshTokenExpiresAt);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        return Ok(new AuthResponse(
            result.AccessToken,
            result.AccessTokenExpiresAt,
            result.User));
    }

    private void SetRefreshTokenCookie(string rawToken, DateTime expiresAt)
    {
        Response.Cookies.Append(
            RefreshCookieName,
            rawToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = expiresAt,
                Path = "/api/auth"
            });
    }

    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Delete(
            RefreshCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/auth"
            });
    }

    private void ValidateTrustedOrigin()
    {
        // Console.WriteLine("Validating request origin...");
        var allowedOrigins = _configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();

        if (allowedOrigins.Length == 0)
            return;

        // Console.WriteLine($"Allowed origins: {string.Join(", ", allowedOrigins)}");
        var origin = Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin))
        {
            var referer = Request.Headers.Referer.ToString();
            if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
            {
                origin = $"{refererUri.Scheme}://{refererUri.Authority}";
            }
        }

        if (string.IsNullOrWhiteSpace(origin))
            throw AppException.Forbidden("Invalid request origin.");

        if (!allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            throw AppException.Forbidden("Invalid request origin.");
    }
}
