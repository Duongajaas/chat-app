using ChatApp.Data;
using ChatApp.Models;
using ChatApp.Services;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Realtime;

public class MessageExpirationWorker(IServiceScopeFactory scopes, ILogger<MessageExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ExpireBatchAsync(stoppingToken);
                await Task.Delay(processed ? 500 : 3000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Message expiration sweeper failed");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task<bool> ExpireBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var expiredMessageIds = await db.Messages.AsNoTracking()
            .Where(m => m.ExpiresAt != null && m.ExpiresAt <= now && m.DeletedAt == null)
            .OrderBy(m => m.ExpiresAt)
            .Select(m => m.Id)
            .Take(50)
            .ToListAsync(ct);

        if (expiredMessageIds.Count == 0) return false;

        foreach (var messageId in expiredMessageIds)
        {
            await ExpireSingleMessageAsync(scopes, messageId, ct);
        }

        return true;
    }

    private static async Task ExpireSingleMessageAsync(IServiceScopeFactory scopeFactory, Guid messageId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var msg = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (msg == null || msg.DeletedAt != null || msg.ExpiresAt == null || msg.ExpiresAt > DateTime.UtcNow) return;

        var conversationId = msg.ConversationId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ConversationLock.AcquireAsync(db, conversationId, ct);

        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message == null || message.DeletedAt != null || message.ExpiresAt == null || message.ExpiresAt > DateTime.UtcNow)
        {
            return;
        }

        message.DeletedAt = DateTime.UtcNow;
        message.Status = MessageStatus.Deleted;

        var pinsRemoved = await db.PinnedMessages.Where(p => p.MessageId == messageId).ExecuteDeleteAsync(ct);
        if (pinsRemoved > 0)
        {
            var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct);
            if (conversation != null)
            {
                conversation.Version++;
                ConversationEvents.Add(db, conversationId, "PinsChanged", new { ConversationId = conversationId, conversation.Version });
            }
        }

        ConversationEvents.Add(db, conversationId, "MessageExpired", new { ConversationId = conversationId, MessageId = messageId });

        var members = await db.ConversationMembers.Where(m => m.ConversationId == conversationId && m.LeftAt == null).ToListAsync(ct);
        foreach (var member in members)
        {
            member.UnreadCount = await db.Messages.CountAsync(m =>
                m.ConversationId == conversationId &&
                m.Sequence > member.LastReadSequence &&
                m.SenderId != member.UserId &&
                m.DeletedAt == null &&
                (m.ExpiresAt == null || m.ExpiresAt > DateTime.UtcNow), ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
