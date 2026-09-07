using System.Security.Cryptography;
using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatApp.Services;

public class FriendLinkService : IFriendLinkService
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly AppUrlsOptions _appUrls;
    private readonly IBlockService _blockService;

    public FriendLinkService(
        AppDbContext db,
        ITokenService tokenService,
        IOptions<AppUrlsOptions> appUrls,
        IBlockService blockService)
    {
        _db = db;
        _tokenService = tokenService;
        _appUrls = appUrls.Value;
        _blockService = blockService;
    }

    public async Task<FriendLinkResponse> GetOrCreateActiveLinkAsync(Guid userId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var activeLink = await _db.FriendLinks
            .Where(link => link.UserId == userId && link.RevokedAt == null)
            .Where(link => link.ExpiresAt == null || link.ExpiresAt > DateTime.UtcNow)
            .Where(link => link.MaxUses == null || link.UsedCount < link.MaxUses)
            .OrderByDescending(link => link.CreatedAt)
            .FirstOrDefaultAsync();

        if (activeLink is not null)
        {
            await transaction.CommitAsync();
            return ToResponse(activeLink);
        }

        var response = await CreateLinkAsync(userId);
        await transaction.CommitAsync();
        return response;
    }

    public async Task<FriendLinkResponse> RegenerateLinkAsync(Guid userId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var activeLinks = await _db.FriendLinks
            .Where(link => link.UserId == userId && link.RevokedAt == null)
            .ToListAsync();

        foreach (var link in activeLinks)
            link.RevokedAt = DateTime.UtcNow;

        var response = await CreateLinkAsync(userId);
        await transaction.CommitAsync();
        return response;
    }

    public async Task RevokeLinkAsync(Guid userId, Guid linkId)
    {
        var link = await _db.FriendLinks
            .FirstOrDefaultAsync(item => item.Id == linkId && item.UserId == userId);

        if (link is null)
            throw AppException.NotFound("Friend link không tồn tại.");

        if (link.RevokedAt is null)
        {
            link.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return;
        }
    }

    public async Task<PublicUserProfileResponse> ResolveLinkAsync(string rawToken, Guid currentUserId)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            throw AppException.NotFound("Link không tồn tại.");

        var tokenHash = _tokenService.HashToken(rawToken);
        var link = await _db.FriendLinks.FirstOrDefaultAsync(item => item.TokenHash == tokenHash);

        if (link is null)
            throw AppException.NotFound("Link không tồn tại.");

        if (!link.IsActive)
            throw AppException.BadRequest("Link đã hết hạn hoặc không còn hiệu lực.");

        if (link.UserId == currentUserId)
            throw AppException.BadRequest("Không thể tự thêm bạn với chính mình.");

        var owner = await _db.Users
            .Where(user => user.Id == link.UserId && user.DeletedAt == null && user.IsActive)
            .Select(user => new { user.Id, user.Username, user.FullName, user.AvatarUrl })
            .FirstOrDefaultAsync();

        if (owner is null)
            throw AppException.NotFound("Người dùng không tồn tại.");

        if (await _blockService.IsBlockedEitherWayAsync(currentUserId, link.UserId))
            throw AppException.Forbidden("Không thể thực hiện hành động này.");

        var lowId = currentUserId.CompareTo(link.UserId) < 0 ? currentUserId : link.UserId;
        var highId = currentUserId.CompareTo(link.UserId) < 0 ? link.UserId : currentUserId;
        var areFriends = await _db.Friendships.AnyAsync(friendship =>
            friendship.UserLowId == lowId && friendship.UserHighId == highId);

        var hasPendingRequest = await _db.FriendRequests.AnyAsync(request =>
            request.Status == FriendRequestStatus.Pending &&
            ((request.SenderId == currentUserId && request.ReceiverId == link.UserId) ||
             (request.SenderId == link.UserId && request.ReceiverId == currentUserId)));

        var incremented = await _db.FriendLinks
            .Where(item => item.Id == link.Id && item.RevokedAt == null)
            .Where(item => item.ExpiresAt == null || item.ExpiresAt > DateTime.UtcNow)
            .Where(item => item.MaxUses == null || item.UsedCount < item.MaxUses)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.UsedCount, item => item.UsedCount + 1));

        if (incremented == 0)
            throw AppException.BadRequest("Link đã hết hạn hoặc không còn hiệu lực.");

        return new PublicUserProfileResponse(
            owner.Id,
            owner.Username,
            owner.FullName,
            owner.AvatarUrl,
            areFriends ? "Friends" : hasPendingRequest ? "Pending" : "None");
    }

    private async Task<FriendLinkResponse> CreateLinkAsync(Guid userId)
    {
        var userExists = await _db.Users.AnyAsync(user => user.Id == userId && user.DeletedAt == null && user.IsActive);
        if (!userExists)
            throw AppException.NotFound("Người dùng không tồn tại.");

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var link = new FriendLink
        {
            UserId = userId,
            Token = rawToken,
            TokenHash = _tokenService.HashToken(rawToken),
            CreatedAt = DateTime.UtcNow
        };

        _db.FriendLinks.Add(link);
        await _db.SaveChangesAsync();
        return ToResponse(link);
    }

    private FriendLinkResponse ToResponse(FriendLink link)
    {
        var baseUrl = _appUrls.FrontendBaseUrl.TrimEnd('/');
        return new FriendLinkResponse(
            link.Id,
            $"{baseUrl}/add-friend/{link.Token}",
            link.ExpiresAt,
            link.MaxUses,
            link.UsedCount,
            link.CreatedAt);
    }
}
