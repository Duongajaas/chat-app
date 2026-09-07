using ChatApp.DTOs;

namespace ChatApp.Services;

public interface IBlockService
{
    Task BlockUserAsync(Guid blockerId, Guid targetUserId);
    Task UnblockUserAsync(Guid blockerId, Guid targetUserId);
    Task<bool> IsBlockedEitherWayAsync(Guid userA, Guid userB);
    Task<List<BlockedUserResponse>> GetBlockedUsersAsync(Guid userId);
}
