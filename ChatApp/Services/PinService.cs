using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Realtime;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public class PinService(AppDbContext db)
{
    public async Task<List<PinnedMessageResponse>> ListAsync(Guid user, Guid id, CancellationToken ct = default)
    {
        var reader = new MessageReader(db);
        await reader.EnsureMembershipAsync(user, id, ct);
        var pins = await db.PinnedMessages.AsNoTracking().Where(p => p.ConversationId == id).OrderBy(p => p.CreatedAt).Take(5).ToListAsync(ct);
        var messages = await reader.ReadAsync(user, id, pins.Select(p => p.MessageId).ToArray(), ct);
        return pins.Where(p => messages.Any(m => m.Id == p.MessageId && m.Status != "Deleted"))
            .Select(p => new PinnedMessageResponse(p.Id, p.PinnedBy, p.CreatedAt, messages.Single(m => m.Id == p.MessageId))).ToList();
    }

    public async Task SetAsync(Guid user, Guid id, Guid messageId, bool pinned, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ConversationLock.AcquireAsync(db, id, ct);
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw AppException.NotFound("Hội thoại không tồn tại.");
        if (conversation.Type == ConversationType.Group)
            await new GroupService(db).AuthorizeAsync(user, id, "PinMessage");
        else if (conversation.ClosedAt != null || conversation.DeletedAt != null ||
            !await db.ConversationMembers.AnyAsync(m => m.ConversationId == id && m.UserId == user && m.LeftAt == null, ct) ||
            await db.DirectConversations.AnyAsync(d => d.ConversationId == id && db.Blocks.Any(b =>
                b.BlockerId == d.UserLowId && b.BlockedId == d.UserHighId || b.BlockerId == d.UserHighId && b.BlockedId == d.UserLowId), ct))
            throw AppException.Forbidden("Không có quyền ghim tin.");
        if (!await new MessageReader(db).Visible(user, id).AnyAsync(m => m.Id == messageId && (!pinned || m.DeletedAt == null), ct))
            throw AppException.NotFound("Tin nhắn không khả dụng.");
        var existing = await db.PinnedMessages.SingleOrDefaultAsync(p => p.ConversationId == id && p.MessageId == messageId, ct);
        if (pinned == (existing != null)) return;
        if (pinned)
        {
            if (await db.PinnedMessages.CountAsync(p => p.ConversationId == id, ct) >= 5)
                throw AppException.Conflict("Nhóm đã có 5 tin ghim, kể cả tin bạn không có quyền đọc.");
            db.PinnedMessages.Add(new PinnedMessage { ConversationId = id, MessageId = messageId, PinnedBy = user });
        }
        else db.PinnedMessages.Remove(existing!);
        new GroupService(db).Changed(conversation, user, pinned ? "PIN_MESSAGE" : "UNPIN_MESSAGE",
            metadata: new { messageId });
        ConversationEvents.Add(db, id, "PinsChanged", new { ConversationId = id, conversation.Version });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
