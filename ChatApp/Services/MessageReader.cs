using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public class MessageReader(AppDbContext db)
{
    public async Task EnsureMembershipAsync(Guid user, Guid conversation, CancellationToken ct = default)
    {
        if (!await db.ConversationMembers.AnyAsync(m => m.UserId == user && m.ConversationId == conversation, ct))
            throw AppException.Forbidden("Không có quyền đọc hội thoại.");
    }

    public IQueryable<Message> Visible(Guid user, Guid conversation) =>
        MessageVisibility.Readable(db, user, conversation).Where(m =>
            !db.MessageDeletions.Any(d => d.MessageId == m.Id && d.UserId == user));

    public async Task<List<MessageResponse>> ReadAsync(Guid user, Guid conversation, Guid[] ids, CancellationToken ct = default)
    {
        await EnsureMembershipAsync(user, conversation, ct);
        if (ids.Length > 100) throw AppException.BadRequest("Tối đa 100 tin nhắn.");
        var rows = await Visible(user, conversation).AsNoTracking().Where(m => ids.Contains(m.Id)).OrderBy(m => m.Sequence).ToListAsync(ct);
        return await BuildAsync(user, conversation, rows, ct);
    }

    // Fixed query count per page; quotes are evaluated for this reader, never for the sender only.
    public async Task<List<MessageResponse>> BuildAsync(Guid user, Guid conversation, List<Message> rows, CancellationToken ct = default)
    {
        var ids = rows.Where(m => m.DeletedAt == null).Select(m => m.Id).ToArray();
        var replyIds = rows.Where(m => m.DeletedAt == null && m.ReplyToMessageId != null).Select(m => m.ReplyToMessageId!.Value).Distinct().ToArray();
        var originals = await Visible(user, conversation).AsNoTracking()
            .Where(m => replyIds.Contains(m.Id) && m.DeletedAt == null).ToDictionaryAsync(m => m.Id, ct);
        var senderIds = originals.Values.Where(m => m.SenderId != null).Select(m => m.SenderId!.Value).ToArray();
        var names = await db.Users.Where(u => senderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var mentions = await db.MessageMentions.Where(m => ids.Contains(m.MessageId)).ToListAsync(ct);
        var attachments = await db.MessageAttachments.Where(a => ids.Contains(a.MessageId)).ToListAsync(ct);
        return rows.Select(m => {
            ReplyPreviewResponse? reply = null;
            if (m.DeletedAt == null && m.ReplyToMessageId is Guid replyId)
            {
                var original = originals.GetValueOrDefault(replyId);
                reply = original == null ? new(replyId, null, null, false) :
                    new(replyId, original.SenderId is Guid sender ? names.GetValueOrDefault(sender) : null,
                        original.Content is { Length: > 80 } text ? text[..80] + "…" : original.Content, true, original.Sequence);
            }
            return MessageService.ToResponse(m) with {
                ReplyToMessageId = m.DeletedAt == null ? m.ReplyToMessageId : null,
                ReplyPreview = reply, IsForwarded = m.DeletedAt == null && m.IsForwarded,
                Mentions = mentions.Where(x => x.MessageId == m.Id).OrderBy(x => x.Start)
                    .Select(x => new MentionResponse(x.MentionedUserId, x.Username, x.Start, x.Length)).ToArray(),
                Attachments = attachments.Where(x => x.MessageId == m.Id)
                    .Select(x => new AttachmentResponse(x.Id, x.FileName, x.FileType, x.FileSize)).ToArray()
            };
        }).ToList();
    }
}
