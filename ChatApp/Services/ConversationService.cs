using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Common;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public class ConversationService : IConversationService
{
    private readonly AppDbContext _db;
    private readonly IBlockService _blockService;

    public ConversationService(AppDbContext dbContext, IBlockService blockService)
    {
        _db = dbContext;
        _blockService = blockService;
    }

    public async Task<ConversationSummaryResponse> GetOrCreateDirectConversationAsync(Guid userId, Guid otherUserId)
    {
        if (userId == otherUserId)
        {
            throw AppException.BadRequest("Không thể tự trò chuyện với chính mình.");
        }

        var userLowId = userId.CompareTo(otherUserId) < 0 ? userId : otherUserId;
        var userHighId = userId.CompareTo(otherUserId) < 0 ? otherUserId : userId;

        var directConversation = await _db.DirectConversations
            .FirstOrDefaultAsync(x => x.UserLowId == userLowId && x.UserHighId == userHighId);

        if (directConversation != null)
        {
            var conversation = await _db.Conversations.FindAsync(directConversation.ConversationId)
                ?? throw AppException.NotFound("Conversation không tồn tại.");

            var conversationMember = await _db.ConversationMembers
                .FirstOrDefaultAsync(x => x.ConversationId == conversation.Id && x.UserId == userId);

            var otherUserName = await _db.Users
                .Where(u => u.Id == otherUserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync();

            return new ConversationSummaryResponse(
                conversation.Id,
                otherUserName,
                conversation.Type,
                conversationMember?.UnreadCount ?? 0,
                conversation.CreatedAt,
                conversation.UpdatedAt,
                conversation.LastMessageId,
                conversation.LastMessageAt,
                await _blockService.IsBlockedEitherWayAsync(userId, otherUserId),
                otherUserId);
        }

        var newConversation = new Conversation
        {
            Type = ConversationType.Direct,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var newDirectConversation = new DirectConversation
        {
            ConversationId = newConversation.Id,
            UserLowId = userLowId,
            UserHighId = userHighId,
            CreatedAt = DateTime.UtcNow
        };

        var members = new[]
        {
            new ConversationMember { ConversationId = newConversation.Id, UserId = userId, JoinedAt = DateTime.UtcNow },
            new ConversationMember { ConversationId = newConversation.Id, UserId = otherUserId, JoinedAt = DateTime.UtcNow }
        };

        var otherUserFullName = await _db.Users
            .Where(u => u.Id == otherUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync();

        _db.ConversationMembers.AddRange(members);
        _db.Conversations.Add(newConversation);
        _db.DirectConversations.Add(newDirectConversation);
        await _db.SaveChangesAsync();

        return new ConversationSummaryResponse(
            newConversation.Id,
            otherUserFullName,
            newConversation.Type,
            0,
            newConversation.CreatedAt,
            newConversation.UpdatedAt,
            null,
            null,
            await _blockService.IsBlockedEitherWayAsync(userId, otherUserId),
            otherUserId);
    }

    public async Task<bool> IsMemberAsync(Guid userId, Guid conversationId)
    {
        var checkMember = await _db.ConversationMembers
            .FirstOrDefaultAsync(x => x.ConversationId == conversationId && x.UserId == userId && x.LeftAt == null);
        return checkMember != null;
    }

    public async Task<List<ConversationSummaryResponse>> GetMyConversationsAsync(Guid userId)
    {
        // Bước 1: lấy dữ liệu thô, sort ngay trên entity (trước khi Select thành record)
        var raw = await _db.ConversationMembers
            .Where(x => x.UserId == userId && x.LeftAt == null && x.HiddenAt == null)
            .Join(_db.Conversations,
                cm => cm.ConversationId,
                c => c.Id,
                (cm, c) => new { cm, c })
            .OrderByDescending(x => x.c.LastMessageAt ?? x.c.CreatedAt)
            .Select(x => new
            {
                x.c.Id,
                x.c.Name,
                x.c.Type,
                x.cm.UnreadCount,
                x.c.CreatedAt,
                x.c.UpdatedAt,
                x.c.LastMessageId,
                x.c.LastMessageAt
            })
            .ToListAsync();

        // Bước 2: với các conversation Direct, gom hết id lại, tra tên "người kia" trong 1 query duy nhất
        var directConversationIds = raw
            .Where(x => x.Type == ConversationType.Direct)
            .Select(x => x.Id)
            .ToList();

        var directPeers = await _db.ConversationMembers
            .Where(cm => directConversationIds.Contains(cm.ConversationId) && cm.UserId != userId)
            .Join(_db.Users,
                cm => cm.UserId,
                u => u.Id,
                (cm, u) => new { cm.ConversationId, UserId = u.Id, u.FullName })
            .ToDictionaryAsync(x => x.ConversationId);

        // Bước 3: ghép lại trong bộ nhớ, direct conversation hỏi trạng thái block qua service chung
        var result = new List<ConversationSummaryResponse>();
        foreach (var x in raw)
        {
            var isDirect = x.Type == ConversationType.Direct;
            var hasPeer = isDirect && directPeers.TryGetValue(x.Id, out var peer);
            var peerUserId = hasPeer ? peer.UserId : (Guid?)null;

            result.Add(new ConversationSummaryResponse(
                x.Id,
                hasPeer ? peer.FullName : x.Name,
                x.Type,
                x.UnreadCount,
                x.CreatedAt,
                x.UpdatedAt,
                x.LastMessageId,
                x.LastMessageAt,
                peerUserId is not null && await _blockService.IsBlockedEitherWayAsync(userId, peerUserId.Value),
                peerUserId));
        }

        return result;
    }

    public async Task<ConversationSummaryResponse> GetConversationByIdAsync(Guid userId, Guid conversationId)
    {
        var isMember = await IsMemberAsync(userId, conversationId);

        if (!isMember)
        {
            throw AppException.BadRequest("User is not a member of this conversation");
        }

        var conversation = await _db.Conversations.FindAsync(conversationId);

        if (conversation == null)
        {
            throw AppException.NotFound("Conversation not found");
        }

        var conversationMember = await _db.ConversationMembers
            .FirstOrDefaultAsync(x => x.ConversationId == conversationId && x.UserId == userId);

        if (conversation.Type == ConversationType.Direct)
        {
            var otherUserId = await _db.ConversationMembers
                .Where(cm => cm.ConversationId == conversation.Id && cm.UserId != userId)
                .Select(cm => cm.UserId)
                .FirstOrDefaultAsync();

            var fullName = await _db.Users
                .Where(u => u.Id == otherUserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync();

            return new ConversationSummaryResponse(
                conversation.Id,
                fullName,
                conversation.Type,
                conversationMember.UnreadCount,
                conversation.CreatedAt,
                conversation.UpdatedAt,
                conversation.LastMessageId,
                conversation.LastMessageAt,
                await _blockService.IsBlockedEitherWayAsync(userId, otherUserId),
                otherUserId);
        }

        return new ConversationSummaryResponse(conversation.Id, conversation.Name, conversation.Type, conversationMember.UnreadCount, conversation.CreatedAt, conversation.UpdatedAt, conversation.LastMessageId, conversation.LastMessageAt);
    }

    public async Task<ConversationSummaryResponse> CreateConversationAsync(Guid userId, CreateConversationRequest request)
    {
        if (request.Type == ConversationType.Direct)
        {
            throw AppException.BadRequest("Only use enpoint to create group conversation");
        }

        var newConversation = new Conversation
        {
            Type = request.Type,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Name = request.Name
        };

        if (request.MemberIds.Length < 2)
        {
            throw AppException.BadRequest("A conversation must have at least 2 members.");
        }

        var allMemberIds = request.MemberIds.Append(userId).Distinct().ToList();

        var members = allMemberIds.Select(memberId => new ConversationMember
        {
            ConversationId = newConversation.Id,
            UserId = memberId,
            Role = memberId == userId ? MemberRole.Owner : MemberRole.Member,
            JoinedAt = DateTime.UtcNow
        }).ToList();

        _db.ConversationMembers.AddRange(members);
        _db.Conversations.Add(newConversation);
        await _db.SaveChangesAsync();

        return new ConversationSummaryResponse(newConversation.Id, null, newConversation.Type, 0, newConversation.CreatedAt, newConversation.UpdatedAt, null, null);
    }

    public async Task DeleteConversationAsync(Guid userId, Guid conversationId)
    {
        var conversationMember = await _db.ConversationMembers
            .FirstOrDefaultAsync(x => x.ConversationId == conversationId && x.UserId == userId);

        if (conversationMember == null)
        {
            throw AppException.Forbidden("User is not a member of this conversation");
        }

        // rời nhóm không xóa lịch sử cũ.
        // Chỉ set LeftAt để loại khỏi active membership, không xóa record conversation_member.
        conversationMember.LeftAt = DateTime.UtcNow;
        conversationMember.HiddenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
