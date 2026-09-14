using System.Text.Json;
using ChatApp.Data;
using ChatApp.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Realtime;

public class OutboxWorker(IServiceScopeFactory scopes, IHubContext<ChatHub> hub, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ids = await db.OutboxEvents.AsNoTracking()
                    .Where(e => e.ProcessedAt == null && e.NextAttemptAt <= DateTime.UtcNow)
                    .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).Select(e => e.Id).Take(100).ToListAsync(stoppingToken);
                foreach (var id in ids) await DeliverAsync(id, stoppingToken);
                await Task.Delay(ids.Count == 0 ? 1000 : 100, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox polling failed");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task DeliverAsync(Guid id, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var candidate = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ConversationLock.AcquireAsync(db, candidate.ConversationId, ct);
        var item = await db.OutboxEvents.SingleAsync(e => e.Id == id, ct);
        if (item.ProcessedAt != null || item.NextAttemptAt > DateTime.UtcNow) return;
        try
        {
            var recipients = item.TargetUserId is Guid userId
                ? new[] { userId.ToString() }
                : await ConversationEvents.RecipientsAsync(db, item.ConversationId, ct);
            // Do not replay original content after a message was recalled before delivery.
            var payload = JsonSerializer.Deserialize<JsonElement>(item.Payload);
            if (item.EventName == "ReceiveMessage" && payload.TryGetProperty("id", out var messageId))
            {
                var deleted = await db.Messages.AnyAsync(m => m.Id == messageId.GetGuid() && m.DeletedAt != null, ct);
                if (deleted) recipients = [];
                else
                {
                    var hiddenFor = await db.MessageDeletions.Where(d => d.MessageId == messageId.GetGuid())
                        .Select(d => d.UserId.ToString()).ToListAsync(ct);
                    recipients = recipients.Except(hiddenFor).ToArray();
                }
            }
            // Membership periods apply to delayed content too, including a user who rejoined.
            if (!await db.DirectConversations.AnyAsync(d => d.ConversationId == item.ConversationId, ct))
            {
                long? sequence = payload.TryGetProperty("sequence", out var seq) ? seq.GetInt64() : null;
                if (sequence == null && payload.TryGetProperty("messageId", out var relatedId))
                    sequence = await db.Messages.Where(m => m.Id == relatedId.GetGuid() && m.ConversationId == item.ConversationId)
                        .Select(m => (long?)m.Sequence).SingleOrDefaultAsync(ct);
                if (sequence.HasValue)
                {
                    var allowed = await db.ConversationMembershipPeriods.Where(p => p.ConversationId == item.ConversationId &&
                        sequence.Value > p.StartSequence && (p.EndSequence == null || sequence.Value <= p.EndSequence))
                        .Select(p => p.UserId.ToString()).ToListAsync(ct);
                    recipients = recipients.Intersect(allowed).ToArray();
                }
            }
            if (recipients.Length > 0)
                await hub.Clients.Users(recipients).SendAsync(item.EventName, payload, ct);
            item.ProcessedAt = DateTime.UtcNow;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            item.Attempts++;
            item.NextAttemptAt = DateTime.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(item.Attempts, 8))));
            logger.LogError(ex, "Outbox delivery failed for {EventId}, attempt {Attempt}", id, item.Attempts);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
