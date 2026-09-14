using System.Text.Json;
using ChatApp.Data;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Realtime;

public static class ConversationEvents
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Add(AppDbContext db, Guid conversationId, string name, object payload, Guid? targetUserId = null) =>
        db.OutboxEvents.Add(new OutboxEvent
        {
            ConversationId = conversationId, EventName = name,
            Payload = JsonSerializer.Serialize(payload, JsonOptions), TargetUserId = targetUserId
        });

    // Call while holding the conversation lock. No conversation group is used for delivery.
    public static async Task<string[]> RecipientsAsync(AppDbContext db, Guid conversationId, CancellationToken ct)
    {
        var direct = await db.DirectConversations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ConversationId == conversationId, ct);
        if (direct != null && await db.Blocks.AnyAsync(b =>
            (b.BlockerId == direct.UserLowId && b.BlockedId == direct.UserHighId) ||
            (b.BlockerId == direct.UserHighId && b.BlockedId == direct.UserLowId), ct))
            return [];
        return await db.ConversationMembers.Where(m => m.ConversationId == conversationId && m.LeftAt == null)
            .Select(m => m.UserId.ToString()).ToArrayAsync(ct);
    }
}
