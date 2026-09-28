using ChatApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Media;

public class AvatarCleanupWorker(IServiceScopeFactory scopes, ILogger<AvatarCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var storage = scope.ServiceProvider.GetRequiredService<IAvatarStorage>();
                if (storage.IsConfigured)
                {
                    // Wait past Cloudinary's one-hour upload signature window before deletion.
                    var ids = await db.AvatarAssets.Where(a => a.DeletedAt == null &&
                        (a.DeletePending || a.DeleteAfter < DateTime.UtcNow ||
                         a.AttachedAt == null && a.CreatedAt < DateTime.UtcNow.AddHours(-2)))
                        .OrderBy(a => a.CreatedAt).Select(a => a.Id).Take(20).ToListAsync(ct);
                    foreach (var id in ids)
                    {
                        db.ChangeTracker.Clear();
                        var asset = await db.AvatarAssets.SingleAsync(a => a.Id == id, ct);
                        await using (var tx = await db.Database.BeginTransactionAsync(ct))
                        {
                            await ConversationLock.AcquireAsync(db, asset.ConversationId, ct);
                            await db.Entry(asset).ReloadAsync(ct);
                            if (asset.DeletedAt != null || !(asset.DeletePending || asset.DeleteAfter < DateTime.UtcNow ||
                                asset.AttachedAt == null && asset.CreatedAt < DateTime.UtcNow.AddHours(-2))) continue;
                            if (await db.Conversations.AnyAsync(c => c.AvatarAssetId == id, ct)) continue;
                            asset.DeletePending = true;
                            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                        }
                        await storage.DeleteAsync(asset.PublicId, ct);
                        asset.DeletedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Avatar cleanup failed; retrying on the next sweep."); }
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
