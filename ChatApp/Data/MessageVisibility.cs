using ChatApp.Models;

namespace ChatApp.Data;

public static class MessageVisibility
{
    // Fail closed for group memberships without a period; migration backfills legacy rows.
    public static IQueryable<Message> Readable(AppDbContext db, Guid userId, Guid conversationId) =>
        db.Messages.Where(m => m.ConversationId == conversationId &&
            (db.DirectConversations.Any(d => d.ConversationId == conversationId) ||
             db.ConversationMembershipPeriods.Any(p => p.ConversationId == conversationId && p.UserId == userId &&
                 m.Sequence > p.StartSequence && (p.EndSequence == null || m.Sequence <= p.EndSequence))));
}
