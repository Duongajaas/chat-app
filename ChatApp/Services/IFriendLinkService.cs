using ChatApp.DTOs;

namespace ChatApp.Services;

public interface IFriendLinkService
{
    Task<FriendLinkResponse> GetOrCreateActiveLinkAsync(Guid userId);
    Task<FriendLinkResponse> RegenerateLinkAsync(Guid userId);
    Task RevokeLinkAsync(Guid userId, Guid linkId);
    Task<PublicUserProfileResponse> ResolveLinkAsync(string rawToken, Guid currentUserId);
}