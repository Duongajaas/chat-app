using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public class FriendService : IFriendService
{
    private readonly AppDbContext _db;
    private readonly IBlockService _blockService;

    public FriendService(AppDbContext db, IBlockService blockService)
    {
        _db = db;
        _blockService = blockService;
    }

    public async Task<FriendRequestResponse> SendRequestAsync(Guid senderId, Guid receiverId)
    {
        if (senderId == receiverId)
            throw AppException.BadRequest("Không thể tự kết bạn với chính mình.");

        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var usersExist = await _db.Users.CountAsync(user =>
            (user.Id == senderId || user.Id == receiverId) && user.DeletedAt == null && user.IsActive);
        if (usersExist != 2)
            throw AppException.NotFound("Người dùng không tồn tại.");

        if (await _blockService.IsBlockedEitherWayAsync(senderId, receiverId))
            throw AppException.Forbidden("Không thể thực hiện hành động này.");

        var lowId = senderId.CompareTo(receiverId) < 0 ? senderId : receiverId;
        var highId = senderId.CompareTo(receiverId) < 0 ? receiverId : senderId;
        var friendshipExists = await _db.Friendships.AnyAsync(friendship =>
            friendship.UserLowId == lowId && friendship.UserHighId == highId);
        if (friendshipExists)
            throw AppException.Conflict("Hai bạn đã là bạn bè.");

        var existingRequest = await _db.FriendRequests
            .Where(request => request.Status == FriendRequestStatus.Pending)
            .Where(request =>
                (request.SenderId == senderId && request.ReceiverId == receiverId) ||
                (request.SenderId == receiverId && request.ReceiverId == senderId))
            .OrderByDescending(request => request.CreatedAt)
            .FirstOrDefaultAsync();

        if (existingRequest is not null)
        {
            await transaction.CommitAsync();
            if (existingRequest.SenderId == senderId)
                return ToResponse(existingRequest);

            throw AppException.Conflict("Người dùng này đã gửi lời mời kết bạn cho bạn.");
        }

        var request = new FriendRequest
        {
            SenderId = senderId,
            ReceiverId = receiverId,
            Status = FriendRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.FriendRequests.Add(request);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return ToResponse(request);
    }

    public async Task<List<PendingFriendRequestResponse>> GetPendingRequestsAsync(Guid receiverId)
    {
        var pendingRequests = await _db.FriendRequests
            .Where(request => request.ReceiverId == receiverId && request.Status == FriendRequestStatus.Pending)
            .Join(_db.Users.Where(u => u.DeletedAt == null && u.IsActive),
                request => request.SenderId,
                user => user.Id,
                (request, user) => new { request, user })          
            .OrderByDescending(x => x.request.CreatedAt)            
            .Select(x => new PendingFriendRequestResponse(            
                x.request.Id,
                x.user.Id,
                x.user.Username,
                x.user.FullName,
                x.user.AvatarUrl,
                x.request.CreatedAt))
            .ToListAsync();

        return pendingRequests;
    }

    public async Task<FriendRequestResponse> AcceptRequestAsync(Guid receiverId, Guid requestId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var request = await _db.FriendRequests
            .FirstOrDefaultAsync(item => item.Id == requestId && item.ReceiverId == receiverId);

        if (request is null)
            throw AppException.NotFound("Lời mời kết bạn không tồn tại.");

        if (request.Status != FriendRequestStatus.Pending)
            throw AppException.Conflict("Lời mời kết bạn này đã được xử lý.");

        if (await _blockService.IsBlockedEitherWayAsync(request.SenderId, request.ReceiverId))
            throw AppException.Forbidden("Không thể thực hiện hành động này.");

        var lowId = request.SenderId.CompareTo(request.ReceiverId) < 0 ? request.SenderId : request.ReceiverId;
        var highId = request.SenderId.CompareTo(request.ReceiverId) < 0 ? request.ReceiverId : request.SenderId;

        var friendshipExists = await _db.Friendships.AnyAsync(friendship =>
            friendship.UserLowId == lowId && friendship.UserHighId == highId);

        if (!friendshipExists)
        {
            _db.Friendships.Add(new Friendship
            {
                UserLowId = lowId,
                UserHighId = highId,
                CreatedAt = DateTime.UtcNow
            });
        }

        request.Status = FriendRequestStatus.Accepted;
        request.RespondedAt = DateTime.UtcNow;

        var directConversation = await _db.DirectConversations
            .FirstOrDefaultAsync(conversation =>
                conversation.UserLowId == lowId && conversation.UserHighId == highId);

        if (directConversation is not null)
        {
            await _db.ConversationMembers
                .Where(member => member.ConversationId == directConversation.ConversationId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(member => member.RequestStatus, MemberRequestStatus.Accepted));
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return ToResponse(request);
    }

    public async Task<List<FriendResponse>> GetFriendsAsync(Guid userId)
    {
        var friendships = await _db.Friendships
            .Where(friendship => friendship.UserLowId == userId || friendship.UserHighId == userId)
            .Select(friendship => new
            {
                friendship.CreatedAt,
                FriendId = friendship.UserLowId == userId
                    ? friendship.UserHighId
                    : friendship.UserLowId
            })
            .ToListAsync();

        var friendIds = friendships.Select(friendship => friendship.FriendId).ToList();
        if (friendIds.Count == 0)
            return [];

        var users = await _db.Users
            .Where(user => friendIds.Contains(user.Id) && user.DeletedAt == null && user.IsActive)
            .ToDictionaryAsync(user => user.Id);

        return friendships
            .Where(friendship => users.ContainsKey(friendship.FriendId))
            .Select(friendship =>
            {
                var user = users[friendship.FriendId];
                return new FriendResponse(
                    user.Id,
                    user.Username,
                    user.FullName,
                    user.AvatarUrl,
                    friendship.CreatedAt);
            })
            .OrderBy(friend => friend.FullName)
            .ToList();
    }

    private static FriendRequestResponse ToResponse(FriendRequest request) => new(
        request.Id,
        request.SenderId,
        request.ReceiverId,
        request.Status.ToString(),
        request.CreatedAt);
}
