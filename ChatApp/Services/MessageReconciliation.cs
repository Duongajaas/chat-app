using System.Text.Json;
using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public class MessageReconciliation(AppDbContext db)
{
    public static string Hash(Guid conversationId, SendMessageRequest request, Guid? sourceId) =>
        // Keep hashes of existing text messages stable across this rollout.
        request.MediaUrl == null && request.VoiceDuration == null && request.WaveformPoints == null
            ? GroupService.Hash(JsonSerializer.Serialize(new { conversationId, request.Content,
                request.ReplyToMessageId, Mentions = request.Mentions ?? [], sourceId }))
            : GroupService.Hash(JsonSerializer.Serialize(new { conversationId, request.Content,
                request.ReplyToMessageId, Mentions = request.Mentions ?? [], request.MediaUrl,
                request.VoiceDuration, request.WaveformPoints, sourceId }));

    public async Task<List<MessageReceipt>> ReconcileAsync(Guid user, Guid conversation, ReconcileItem[] items, CancellationToken ct)
    {
        if (items.Length is < 1 or > 100 || items.Any(i => i == null || i.ClientMessageId == Guid.Empty))
            throw AppException.BadRequest("Danh sách đối soát không hợp lệ.");
        var keys = items.Select(i => i.ClientMessageId).ToArray();
        var existing = await db.Messages.AsNoTracking().Where(m => m.SenderId == user && keys.Contains(m.ClientMessageId))
            .ToDictionaryAsync(m => m.ClientMessageId, ct);
        var allowed = await MessageVisibility.Readable(db, user, conversation)
            .Where(m => m.SenderId == user && keys.Contains(m.ClientMessageId))
            .Select(m => m.Id).ToListAsync(ct);
        // Direct visibility also needs membership; receipt lookup itself is limited to the sender.
        if (!await db.ConversationMembers.AnyAsync(m => m.ConversationId == conversation && m.UserId == user, ct)) allowed.Clear();
        var hidden = await db.MessageDeletions.Where(d => d.UserId == user && allowed.Contains(d.MessageId)).Select(d => d.MessageId).ToListAsync(ct);
        var readable = existing.Values.Where(m => m.ConversationId == conversation && allowed.Contains(m.Id) && !hidden.Contains(m.Id)).ToList();
        var responses = (await new MessageReader(db).BuildAsync(user, conversation, readable, ct)).ToDictionary(m => m.Id);
        return items.Select(item => {
            if (!existing.TryGetValue(item.ClientMessageId, out var message)) return new MessageReceipt(item.ClientMessageId, "NotFound");
            var request = new SendMessageRequest(item.Content, item.ReplyToMessageId, item.Mentions,
                item.MediaUrl, item.VoiceDuration, item.WaveformPoints);
            var matches = message.RequestHash != null ? message.RequestHash == Hash(conversation, request, item.SourceMessageId)
                : item.SourceMessageId == null && item.ReplyToMessageId == message.ReplyToMessageId && (item.Mentions?.Length ?? 0) == 0 && message.Content == item.Content?.Trim();
            if (message.ConversationId != conversation || !matches) return new MessageReceipt(item.ClientMessageId, "Conflict");
            if (!allowed.Contains(message.Id)) return new MessageReceipt(item.ClientMessageId, "Unavailable");
            if (hidden.Contains(message.Id)) return new MessageReceipt(item.ClientMessageId, "Hidden", message.Id);
            return new MessageReceipt(item.ClientMessageId, message.DeletedAt == null ? "Readable" : "Recalled", message.Id, responses[message.Id]);
        }).ToList();
    }

    public async Task<MessageResponse> ExistingResponseAsync(Guid user, Message message, CancellationToken ct)
    {
        var allowed = await db.ConversationMembers.AnyAsync(m => m.ConversationId == message.ConversationId && m.UserId == user, ct) &&
            await MessageVisibility.Readable(db, user, message.ConversationId).AnyAsync(m => m.Id == message.Id, ct);
        var hidden = allowed && await db.MessageDeletions.AnyAsync(d => d.UserId == user && d.MessageId == message.Id, ct);
        if (!allowed || hidden)
            return new(message.Id, message.ConversationId, user, 0, null, message.ClientMessageId, DateTime.UnixEpoch,
                Status: hidden ? "Hidden" : "Unavailable");
        return (await new MessageReader(db).ReadAsync(user, message.ConversationId, [message.Id], ct)).SingleOrDefault()
            ?? new(message.Id, message.ConversationId, user, 0, null, message.ClientMessageId, DateTime.UnixEpoch, Status: "Unavailable");
    }
}
