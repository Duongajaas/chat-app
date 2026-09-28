using ChatApp.Common;
using ChatApp.Data;
using ChatApp.Media;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public record AvatarIntentResponse(Guid AssetId, DateTime ExpiresAt, string UploadUrl, Dictionary<string, string> Fields);
public class AvatarService(AppDbContext db, IAvatarStorage storage)
{
    public async Task<AvatarIntentResponse> PrepareAsync(Guid user, Guid groupId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ConversationLock.AcquireAsync(db, groupId, ct);
        await new GroupService(db).AuthorizeAsync(user, groupId, "ChangeAvatar");
        if (await db.AvatarAssets.CountAsync(a => a.ConversationId == groupId && a.UploadedBy == user && a.AttachedAt == null &&
            a.DeletedAt == null && a.ExpiresAt > DateTime.UtcNow, ct) >= 3)
            throw AppException.TooManyRequests("Bạn đang có 3 ảnh chờ hoàn tất.");
        var asset = new AvatarAsset { ConversationId = groupId, UploadedBy = user };
        asset.PublicId = $"chatapp/avatars/{groupId:N}/{asset.Id:N}";
        var upload = storage.Prepare(asset.PublicId);
        db.AvatarAssets.Add(asset); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(asset.Id, asset.ExpiresAt, upload.Url, upload.Fields);
    }

    public async Task ApplyAsync(Guid user, Guid groupId, Guid assetId, long expectedVersion, CancellationToken ct = default)
    {
        var candidate = await db.AvatarAssets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == assetId && a.UploadedBy == user && a.ConversationId == groupId, ct)
            ?? throw AppException.NotFound("Ảnh không thuộc yêu cầu upload của bạn.");
        await new GroupService(db).AuthorizeAsync(user, groupId, "ChangeAvatar");
        // Remote I/O is outside the mutation transaction; authorization is checked again afterwards.
        if (candidate.DeletedAt != null || candidate.DeletePending) throw AppException.BadRequest("Ảnh đã hết hiệu lực.");
        var verified = await storage.InspectAsync(candidate.PublicId, ct);
        if (verified.PublicId != candidate.PublicId || string.IsNullOrWhiteSpace(verified.AssetId) ||
            verified.Bytes is < 1 or > 5242880 || verified.Width is < 1 or > 512 || verified.Height is < 1 or > 512 ||
            verified.Format is not ("jpg" or "png" or "webp"))
            throw AppException.BadRequest("Avatar phải là JPEG/PNG/WebP hợp lệ, tối đa 5 MB và 512×512 sau chuẩn hóa.");
        // Discard tracked authorization snapshot before taking the lock.
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ConversationLock.AcquireAsync(db, groupId, ct);
        var (group, _) = await new GroupService(db).AuthorizeAsync(user, groupId, "ChangeAvatar");
        var asset = await db.AvatarAssets.SingleAsync(a => a.Id == assetId, ct);
        if (group.AvatarAssetId == assetId) return;
        if (group.Version != expectedVersion) throw AppException.Conflict("Nhóm vừa thay đổi. Tải lại thông tin rồi thử lại.");
        if (asset.AttachedAt != null || asset.ExpiresAt <= DateTime.UtcNow || asset.DeletePending || asset.DeletedAt != null)
            throw AppException.BadRequest("Yêu cầu upload đã hết hiệu lực.");
        if (group.AvatarAssetId is Guid old)
            await db.AvatarAssets.Where(a => a.Id == old).ExecuteUpdateAsync(s => s.SetProperty(a => a.DeleteAfter, DateTime.UtcNow.AddHours(2)), ct);
        asset.ProviderAssetId = verified.AssetId; asset.ProviderVersion = verified.Version; asset.DeliveryUrl = verified.Url;
        asset.VerifiedAt = asset.AttachedAt = DateTime.UtcNow;
        group.AvatarAssetId = asset.Id; group.AvatarUrl = verified.Url;
        new GroupService(db).Changed(group, user, "CHANGE_AVATAR", metadata: new { assetId });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
