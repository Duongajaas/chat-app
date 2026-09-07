using ChatApp.DTOs;

namespace ChatApp.Services;

public interface IFriendService
{
    Task<FriendRequestResponse> SendRequestAsync(Guid senderId, Guid receiverId);
    Task<List<PendingFriendRequestResponse>> GetPendingRequestsAsync(Guid receiverId);
    Task<FriendRequestResponse> AcceptRequestAsync(Guid receiverId, Guid requestId);
    Task<List<FriendResponse>> GetFriendsAsync(Guid userId);
}