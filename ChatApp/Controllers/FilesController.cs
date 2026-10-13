using System.Security.Claims;
using ChatApp.Common;
using ChatApp.Data;
using ChatApp.Media;
using ChatApp.Models;
using ChatApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Controllers;

[ApiController, Authorize]
public class FilesController(AppDbContext db, IPrivateMediaStorage storage) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("api/files/upload"), EnableRateLimiting("media-upload")]
    [RequestSizeLimit(MediaValidation.MaxBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = MediaValidation.MaxBytes)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length is <= 0 or > MediaValidation.MaxBytes) throw AppException.BadRequest("File phải có dung lượng từ 1 byte đến 8 MB.");
        using var output = new MemoryStream();
        await file.CopyToAsync(output, ct);
        var bytes = output.ToArray();
        if (bytes.LongLength != file.Length || bytes.Length > MediaValidation.MaxBytes) throw AppException.BadRequest("Dung lượng file không hợp lệ.");
        var type = MediaValidation.Detect(bytes);
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (name.Length > 200) name = name[^200..];
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        var asset = new PrivateMediaAsset { OwnerId = UserId, FileName = name, ContentType = type, Length = bytes.Length };
        asset.StorageKey = "chatapp_private/" + asset.Id.ToString("N");
        // Reserve quota before contacting storage, shared across API instances.
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await ConversationLock.AcquireKeyAsync(db, "media-upload:" + UserId, ct);
            var since = DateTime.UtcNow.AddDays(-1);
            var used = await db.PrivateMediaAssets.Where(a => a.OwnerId == UserId && a.CreatedAt > since).SumAsync(a => (long?)a.Length, ct) ?? 0;
            if (used + asset.Length > 100L * 1024 * 1024) throw AppException.TooManyRequests("Đã đạt giới hạn upload 100 MB trong 24 giờ.");
            db.PrivateMediaAssets.Add(asset);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        await storage.UploadAsync(asset.StorageKey, bytes, ct);
        asset.Uploaded = true;
        await db.SaveChangesAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { assetId = asset.Id, url = "/api/media/assets/" + asset.Id, fileName = asset.FileName, contentType = type });
    }

    [HttpGet("api/messages/{messageId:guid}/attachments/{attachmentId:guid}"), EnableRateLimiting("media-read")]
    public async Task<IActionResult> Download(Guid messageId, Guid attachmentId, CancellationToken ct)
    {
        var conversation = await db.Messages.Where(m => m.Id == messageId).Select(m => (Guid?)m.ConversationId).SingleOrDefaultAsync(ct)
            ?? throw AppException.NotFound("Media không khả dụng.");
        var reader = new MessageReader(db);
        await reader.EnsureMembershipAsync(UserId, conversation, ct);
        if (!await reader.Visible(UserId, conversation).AnyAsync(m => m.Id == messageId && m.DeletedAt == null, ct))
            throw AppException.NotFound("Media không khả dụng.");
        var key = await db.MessageAttachments.Where(a => a.Id == attachmentId && a.MessageId == messageId).Select(a => a.StorageKey).SingleOrDefaultAsync(ct);
        var asset = await db.PrivateMediaAssets.SingleOrDefaultAsync(a => a.StorageKey == key && a.Uploaded, ct)
            ?? throw AppException.NotFound("Media không khả dụng.");
        var bytes = await storage.DownloadAsync(asset.StorageKey, asset.Length, ct);
        // A recall, hide or session revocation may have happened during the storage request.
        if (!await SessionAccess.IsActiveAsync(db, User, ct) ||
            !await reader.Visible(UserId, conversation).AnyAsync(m => m.Id == messageId && m.DeletedAt == null, ct))
            throw AppException.NotFound("Media không khả dụng.");
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        return File(bytes, asset.ContentType, asset.FileName);
    }
}
