using System.Security.Claims;
using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Options;
using ChatApp.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ChatApp.Tests;

public class SecurityIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static TokenService Tokens() => new(Microsoft.Extensions.Options.Options.Create(new JwtOptions
        { SecretKey = "test-security-signing-key-only-012345678901", Issuer = "tests", Audience = "tests" }));
    private static AuthService Auth(AppDbContext db, GooglePayload? payload = null)
    {
        var google = new Mock<IGoogleAuthService>();
        if (payload != null) google.Setup(g => g.VerifyIdTokenAsync(It.IsAny<string>())).ReturnsAsync(payload);
        return new(db, Tokens(), google.Object, Mock.Of<IEmailService>(), Microsoft.Extensions.Options.Options.Create(new AccountLockoutOptions()));
    }
    private async Task<(User User, UserDevice Device, Guid Family, string Raw)> Seed()
    {
        await using var db = fixture.Open();
        var user = new User { Username = Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.test", FullName = "Security",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OriginalPassword123", 4) };
        var device = new UserDevice { UserId = user.Id, DeviceToken = Guid.NewGuid().ToString() };
        var refresh = Tokens().GenerateRefreshToken(); var family = Guid.NewGuid();
        db.Users.Add(user); db.UserDevices.Add(device);
        db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, DeviceId = device.Id, FamilyId = family, TokenHash = refresh.TokenHash, ExpiresAt = refresh.ExpiresAt });
        await db.SaveChangesAsync();
        return (user, device, family, refresh.RawToken);
    }
    private static ClaimsPrincipal Principal(Guid user, Guid session) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, user.ToString()), new Claim("sid", session.ToString())], "test"));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Google_cannot_merge_existing_local_account_even_if_local_email_is_verified(bool verified)
    {
        var seed = await Seed();
        await using var db = fixture.Open();
        var existing = await db.Users.SingleAsync(u => u.Id == seed.User.Id); existing.IsVerified = verified; await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<AppException>(() => Auth(db, new("google-sub", seed.User.Email!, "Victim", null, true))
            .GoogleLoginAsync(new("token"), null, null));
        Assert.Equal(409, error.StatusCode);
        await db.Entry(existing).ReloadAsync();
        Assert.Null(existing.GoogleId);
        Assert.Equal(seed.User.PasswordHash, existing.PasswordHash);
        Assert.Single(await db.RefreshTokens.Where(t => t.UserId == seed.User.Id).ToArrayAsync());
    }

    [Fact]
    public async Task Unverified_google_email_cannot_create_a_session()
    {
        await using var db = fixture.Open();
        var error = await Assert.ThrowsAsync<AppException>(() => Auth(db, new("sub", Guid.NewGuid() + "@example.test", "Name", null, false))
            .GoogleLoginAsync(new("token"), null, null));
        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public async Task Logout_with_rotated_ancestor_revokes_entire_family_but_not_other_session()
    {
        var seed = await Seed();
        await using (var db = fixture.Open()) await Auth(db).RefreshTokenAsync(seed.Raw, null, null);
        var other = Guid.NewGuid();
        await using (var db = fixture.Open())
        {
            db.RefreshTokens.Add(new RefreshToken { UserId = seed.User.Id, FamilyId = other, TokenHash = Guid.NewGuid().ToString(), ExpiresAt = DateTime.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
            await Auth(db).LogoutAsync(seed.Raw);
        }
        await using var verify = fixture.Open();
        Assert.False(await SessionAccess.IsActiveAsync(verify, Principal(seed.User.Id, seed.Family)));
        Assert.True(await SessionAccess.IsActiveAsync(verify, Principal(seed.User.Id, other)));
        Assert.Equal(new[] { other.ToString() }, await SessionAccess.RecipientSessionsAsync(verify, [seed.User.Id.ToString()]));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Concurrent_refresh_and_revoke_cannot_leave_a_live_child(bool logout)
    {
        var seed = await Seed();
        await using var gate = fixture.Open();
        await using var tx = await gate.Database.BeginTransactionAsync();
        await SessionAccess.LockAsync(gate, seed.User.Id);
        async Task Refresh()
        {
            await using var db = fixture.Open();
            try { await Auth(db).RefreshTokenAsync(seed.Raw, null, null); }
            catch (AppException e) when (e.StatusCode is 401 or 403) { }
        }
        async Task Revoke()
        {
            await using var db = fixture.Open();
            if (logout) await Auth(db).LogoutAsync(seed.Raw);
            else await Auth(db).RevokeDeviceAsync(seed.User.Id, seed.Device.Id);
        }
        var refresh = Refresh(); var revoke = Revoke();
        await Task.Delay(150);
        Assert.False(refresh.IsCompleted); Assert.False(revoke.IsCompleted);
        await tx.CommitAsync();
        await Task.WhenAll(refresh, revoke).WaitAsync(TimeSpan.FromSeconds(15));
        await using var verify = fixture.Open();
        Assert.False(await SessionAccess.Active(verify).AnyAsync(t => t.FamilyId == seed.Family));
        Assert.All(await verify.RefreshTokens.Where(t => t.FamilyId == seed.Family).ToArrayAsync(), t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task Password_reset_is_single_use_and_revokes_all_sessions_and_other_reset_links()
    {
        var seed = await Seed(); var raw = Guid.NewGuid().ToString();
        await using (var db = fixture.Open())
        {
            db.PasswordResetTokens.AddRange(new PasswordResetToken { UserId = seed.User.Id, TokenHash = Tokens().HashToken(raw), ExpiresAt = DateTime.UtcNow.AddMinutes(10) },
                new PasswordResetToken { UserId = seed.User.Id, TokenHash = Guid.NewGuid().ToString(), ExpiresAt = DateTime.UtcNow.AddMinutes(10) });
            await db.SaveChangesAsync();
        }
        async Task<bool> Reset(string password)
        {
            await using var db = fixture.Open();
            try { await Auth(db).ResetPasswordAsync(new(raw, password)); return true; }
            catch (AppException e) when (e.StatusCode == 400) { return false; }
        }
        var outcomes = await Task.WhenAll(Reset("NewPasswordFirst123"), Reset("NewPasswordSecond123"));
        Assert.Single(outcomes, x => x);
        await using var verify = fixture.Open();
        Assert.False(await SessionAccess.Active(verify).AnyAsync(t => t.UserId == seed.User.Id));
        Assert.False(await verify.PasswordResetTokens.AnyAsync(t => t.UserId == seed.User.Id && t.UsedAt == null));
    }
}
