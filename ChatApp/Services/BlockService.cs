using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Realtime;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public class BlockService : IBlockService
{
    private readonly AppDbContext _db;

    public BlockService(AppDbContext db)
    {
        _db = db;
    }

    public async Task BlockUserAsync(Guid blockerId, Guid targetUserId)
    {
        if (blockerId == targetUserId)
            throw AppException.BadRequest("Không thể tự block chính mình.");

        var targetExists = await _db.Users.AnyAsync(user =>
            user.Id == targetUserId && user.DeletedAt == null && user.IsActive);
        if (!targetExists)
            throw AppException.NotFound("Người dùng không tồn tại.");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        await ConversationLock.AcquireKeyAsync(_db, "presence-privacy");
        var directId = await _db.DirectConversations.Where(d =>
            (d.UserLowId == blockerId && d.UserHighId == targetUserId) ||
            (d.UserHighId == blockerId && d.UserLowId == targetUserId)).Select(d => (Guid?)d.ConversationId).SingleOrDefaultAsync();
        if (directId.HasValue) await ConversationLock.AcquireAsync(_db, directId.Value);
        var now = DateTime.UtcNow;

        var alreadyBlocked = await _db.Blocks.AnyAsync(block =>
            block.BlockerId == blockerId && block.BlockedId == targetUserId);
        if (alreadyBlocked)
        {
            await transaction.CommitAsync();
            return;
        }

        var cooldown = await _db.BlockCooldowns.AsNoTracking().SingleOrDefaultAsync(c => c.ActorId == blockerId && c.TargetId == targetUserId);
        if (cooldown?.ReblockAllowedAt > now)
        {
            var seconds = (int)Math.Ceiling((cooldown.ReblockAllowedAt - now).TotalSeconds);
            throw new AppException($"Bạn cần chờ thêm {seconds / 60} phút {seconds % 60} giây mới được chặn lại.", 409)
            { Code = "REBLOCK_COOLDOWN", ReblockAllowedAt = cooldown.ReblockAllowedAt, RemainingSeconds = seconds };
        }

        var lowId = blockerId.CompareTo(targetUserId) < 0 ? blockerId : targetUserId;
        var highId = blockerId.CompareTo(targetUserId) < 0 ? targetUserId : blockerId;

        _db.Blocks.Add(new Block
        {
            BlockerId = blockerId,
            BlockedId = targetUserId,
            CreatedAt = now
        });

        await _db.Friendships
            .Where(friendship => friendship.UserLowId == lowId && friendship.UserHighId == highId)
            .ExecuteDeleteAsync();

        await _db.FriendRequests
            .Where(request => request.Status == FriendRequestStatus.Pending)
            .Where(request =>
                (request.SenderId == blockerId && request.ReceiverId == targetUserId) ||
                (request.SenderId == targetUserId && request.ReceiverId == blockerId))
            .ExecuteUpdateAsync(update => update
                .SetProperty(request => request.Status, FriendRequestStatus.Rejected)
                .SetProperty(request => request.RespondedAt, (DateTime?)now));

        if (directId.HasValue)
        {
            await _db.Conversations.Where(c => c.Id == directId.Value).ExecuteUpdateAsync(s => s.SetProperty(c => c.Version, c => c.Version + 1));
            ConversationEvents.Add(_db, directId.Value, "ConversationChanged", new { ConversationId = directId.Value });
        }
        ConversationEvents.Add(_db, directId ?? Guid.Empty, "PresenceInvalidated", new { UserId = targetUserId }, blockerId);
        ConversationEvents.Add(_db, directId ?? Guid.Empty, "PresenceInvalidated", new { UserId = blockerId }, targetUserId);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task UnblockUserAsync(Guid blockerId, Guid targetUserId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await ConversationLock.AcquireKeyAsync(_db, "presence-privacy");
        var block = await _db.Blocks.FirstOrDefaultAsync(item =>
            item.BlockerId == blockerId && item.BlockedId == targetUserId);

        if (block is null) { await transaction.CommitAsync(); return; }
        var directId = await _db.DirectConversations.Where(d =>
            (d.UserLowId == blockerId && d.UserHighId == targetUserId) ||
            (d.UserHighId == blockerId && d.UserLowId == targetUserId)).Select(d => (Guid?)d.ConversationId).SingleOrDefaultAsync();
        if (directId.HasValue) await ConversationLock.AcquireAsync(_db, directId.Value);
        var now = DateTime.UtcNow;
        var cooldown = await _db.BlockCooldowns.SingleOrDefaultAsync(c => c.ActorId == blockerId && c.TargetId == targetUserId);
        if (cooldown == null) { cooldown = new BlockCooldown { ActorId = blockerId, TargetId = targetUserId }; _db.BlockCooldowns.Add(cooldown); }
        cooldown.LastUnblockedAt = now; cooldown.ReblockAllowedAt = now.AddHours(1);
        _db.Blocks.Remove(block);
        if (directId.HasValue)
        {
            await _db.Conversations.Where(c => c.Id == directId.Value).ExecuteUpdateAsync(s => s.SetProperty(c => c.Version, c => c.Version + 1));
            ConversationEvents.Add(_db, directId.Value, "ConversationChanged", new { ConversationId = directId.Value });
        }
        ConversationEvents.Add(_db, Guid.Empty, "PresenceInvalidated", new { UserId = targetUserId }, blockerId);
        ConversationEvents.Add(_db, Guid.Empty, "PresenceInvalidated", new { UserId = blockerId }, targetUserId);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<bool> IsBlockedEitherWayAsync(Guid userA, Guid userB)
    {
        return await _db.Blocks.AnyAsync(block =>
            (block.BlockerId == userA && block.BlockedId == userB) ||
            (block.BlockerId == userB && block.BlockedId == userA));
    }

    public async Task<List<BlockedUserResponse>> GetBlockedUsersAsync(Guid userId)
    {
        var result = await _db.Blocks
        .Where(block => block.BlockerId == userId)
        .Join(
            _db.Users.Where(user => user.DeletedAt == null),
            block => block.BlockedId,
            user => user.Id,
            (block, user) => new
            {
                User = user,
                BlockedAt = block.CreatedAt
            })
        .OrderByDescending(x => x.BlockedAt)
        .Select(x => new BlockedUserResponse(
            x.User.Id,
            x.User.Username,
            x.User.FullName,
            x.User.AvatarUrl,
            x.BlockedAt
        ))
        .ToListAsync();

        return result;

        // return await _db.Blocks
        //     .Where(block => block.BlockerId == userId)
        //     .Join(_db.Users.Where(user => user.DeletedAt == null),
        //         block => block.BlockedId,
        //         user => user.Id,
        //         (block, user) => new BlockedUserResponse(
        //             user.Id,
        //             user.Username,
        //             user.FullName,
        //             user.AvatarUrl,
        //             block.CreatedAt))
        //     .OrderByDescending(user => user.BlockedAt)
        //     .ToListAsync();
    }
}
