using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public interface IReportService
{
    Task<ReportResponse> SubmitReportAsync(Guid reporterId, CreateReportRequest request, CancellationToken ct = default);
    Task<List<ReportSummaryResponse>> GetReportsAsync(Guid moderatorId, ReportFilterRequest filter, CancellationToken ct = default);
    Task<ReportResponse> GetReportDetailsAsync(Guid moderatorId, Guid reportId, CancellationToken ct = default);
    Task<ReportResponse> UpdateStatusAsync(Guid moderatorId, Guid reportId, ReportStatus status, CancellationToken ct = default);
    Task<ReportResponse> ApplyDecisionAsync(Guid moderatorId, Guid reportId, ModerationDecisionRequest request, CancellationToken ct = default);
    Task RestrictUserAsync(Guid moderatorId, Guid targetUserId, int minutes, string reason, CancellationToken ct = default);
    Task UnrestrictUserAsync(Guid moderatorId, Guid targetUserId, CancellationToken ct = default);
}

public class ReportService(AppDbContext db, IAuditService auditService, IMessageEncryptionService? encryption = null) : IReportService
{
    private readonly IMessageEncryptionService _encryption = encryption ?? AesGcmMessageEncryptionService.Default;
    public async Task<ReportResponse> SubmitReportAsync(Guid reporterId, CreateReportRequest request, CancellationToken ct = default)
    {
        string? targetPreview = null;
        Guid? targetUserId = null;

        if (request.TargetType == ReportTargetType.User)
        {
            if (request.TargetId == reporterId)
                throw AppException.BadRequest("Không thể tự báo cáo tài khoản của chính mình.");

            var targetUser = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == request.TargetId && u.DeletedAt == null, ct)
                ?? throw AppException.NotFound("Người dùng không tồn tại.");
            targetUserId = targetUser.Id;
            targetPreview = $"{targetUser.FullName} (@{targetUser.Username})";
        }
        else if (request.TargetType == ReportTargetType.Message)
        {
            var message = await db.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == request.TargetId, ct)
                ?? throw AppException.NotFound("Tin nhắn không tồn tại.");

            if (message.SenderId == reporterId)
                throw AppException.BadRequest("Không thể tự báo cáo tin nhắn của chính mình.");

            if (!await db.ConversationMembers.AnyAsync(m => m.ConversationId == message.ConversationId && m.UserId == reporterId, ct))
                throw AppException.Forbidden("Bạn không có quyền truy cập hoặc báo cáo tin nhắn này.");

            targetUserId = message.SenderId;
            var decrypted = message.Content != null ? _encryption.Decrypt(message.Content) : null;
            targetPreview = decrypted is { Length: > 100 } txt ? txt[..100] + "…" : decrypted ?? "[Media/Voice]";
        }
        else if (request.TargetType == ReportTargetType.Conversation)
        {
            var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.TargetId && c.DeletedAt == null, ct)
                ?? throw AppException.NotFound("Hội thoại không tồn tại.");

            if (!await db.ConversationMembers.AnyAsync(m => m.ConversationId == request.TargetId && m.UserId == reporterId, ct))
                throw AppException.Forbidden("Bạn không phải thành viên của hội thoại này.");

            targetPreview = conversation.Name ?? "Hội thoại";
        }

        // Chống gửi trùng lặp báo cáo khi đang chờ xử lý
        var existingPending = await db.Reports.AnyAsync(r =>
            r.ReporterId == reporterId &&
            r.TargetType == request.TargetType &&
            r.TargetId == request.TargetId &&
            (r.Status == ReportStatus.Pending || r.Status == ReportStatus.Reviewing), ct);

        if (existingPending)
            throw AppException.Conflict("Bạn đã gửi báo cáo cho nội dung này và báo cáo đang được chờ xét duyệt.");

        // Kiểm tra ngưỡng tự động gắn cờ tài khoản đáng ngờ (>= 5 báo cáo trong 24h)
        if (targetUserId.HasValue)
        {
            var since = DateTime.UtcNow.AddHours(-24);
            var recentCount = await db.Reports.CountAsync(r =>
                r.TargetType == ReportTargetType.User && r.TargetId == targetUserId.Value && r.CreatedAt > since, ct);

            if (recentCount + 1 >= 5)
            {
                var userToFlag = await db.Users.FindAsync(new object[] { targetUserId.Value }, ct);
                if (userToFlag != null && !userToFlag.IsFlagged)
                {
                    userToFlag.IsFlagged = true;
                    await auditService.LogAsync(null, reporterId, "FLAG_ACCOUNT_AUTOMATIC", userToFlag.Id,
                        new { reason = "Multiple reports received within 24h", count = recentCount + 1 }, ct);
                }
            }
        }

        var report = new Report
        {
            ReporterId = reporterId,
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            Reason = request.Reason.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Status = ReportStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        db.Reports.Add(report);
        await db.SaveChangesAsync(ct);

        var reporter = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == reporterId, ct);
        return new ReportResponse(
            report.Id,
            report.ReporterId,
            reporter?.Username,
            reporter?.FullName,
            report.TargetType,
            report.TargetId,
            targetPreview,
            report.Reason,
            report.Description,
            report.Status,
            null,
            null,
            null,
            report.CreatedAt
        );
    }

    public async Task<List<ReportSummaryResponse>> GetReportsAsync(Guid moderatorId, ReportFilterRequest filter, CancellationToken ct = default)
    {
        await EnsureModeratorAsync(moderatorId, ct);

        var query = db.Reports.AsNoTracking();
        if (filter.Status.HasValue) query = query.Where(r => r.Status == filter.Status.Value);
        if (filter.TargetType.HasValue) query = query.Where(r => r.TargetType == filter.TargetType.Value);

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 50);

        var reports = await query.OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var reporterIds = reports.Select(r => r.ReporterId).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(u => reporterIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        return reports.Select(r => new ReportSummaryResponse(
            r.Id,
            r.ReporterId,
            users.GetValueOrDefault(r.ReporterId),
            r.TargetType,
            r.TargetId,
            r.Reason,
            r.Status,
            r.CreatedAt
        )).ToList();
    }

    public async Task<ReportResponse> GetReportDetailsAsync(Guid moderatorId, Guid reportId, CancellationToken ct = default)
    {
        await EnsureModeratorAsync(moderatorId, ct);

        var report = await db.Reports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw AppException.NotFound("Báo cáo không tồn tại.");

        var reporter = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == report.ReporterId, ct);
        string? reviewerUsername = null;
        if (report.ReviewedBy.HasValue)
        {
            var reviewer = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == report.ReviewedBy.Value, ct);
            reviewerUsername = reviewer?.Username;
        }

        string? targetPreview = await ResolveTargetPreviewAsync(report.TargetType, report.TargetId, ct);

        return new ReportResponse(
            report.Id,
            report.ReporterId,
            reporter?.Username,
            reporter?.FullName,
            report.TargetType,
            report.TargetId,
            targetPreview,
            report.Reason,
            report.Description,
            report.Status,
            report.ReviewedBy,
            reviewerUsername,
            report.ReviewedAt,
            report.CreatedAt
        );
    }

    public async Task<ReportResponse> UpdateStatusAsync(Guid moderatorId, Guid reportId, ReportStatus status, CancellationToken ct = default)
    {
        await EnsureModeratorAsync(moderatorId, ct);

        var report = await db.Reports.SingleOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw AppException.NotFound("Báo cáo không tồn tại.");

        var oldStatus = report.Status;
        report.Status = status;
        report.ReviewedBy = moderatorId;
        report.ReviewedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await auditService.LogAsync(null, moderatorId, "UPDATE_REPORT_STATUS", null,
            new { reportId, oldStatus = oldStatus.ToString(), newStatus = status.ToString() }, ct);

        return await GetReportDetailsAsync(moderatorId, reportId, ct);
    }

    public async Task<ReportResponse> ApplyDecisionAsync(Guid moderatorId, Guid reportId, ModerationDecisionRequest request, CancellationToken ct = default)
    {
        await EnsureModeratorAsync(moderatorId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var report = await db.Reports.SingleOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw AppException.NotFound("Báo cáo không tồn tại.");

        report.Status = request.Status;
        report.ReviewedBy = moderatorId;
        report.ReviewedAt = DateTime.UtcNow;

        Guid? targetUserId = null;
        if (report.TargetType == ReportTargetType.User)
        {
            targetUserId = report.TargetId;
        }
        else if (report.TargetType == ReportTargetType.Message)
        {
            var message = await db.Messages.SingleOrDefaultAsync(m => m.Id == report.TargetId, ct);
            if (message != null)
            {
                targetUserId = message.SenderId;
                if (request.DeleteOffendingMessage && message.DeletedAt == null)
                {
                    message.DeletedAt = DateTime.UtcNow;
                    message.Status = MessageStatus.Deleted;
                    Realtime.ConversationEvents.Add(db, message.ConversationId, "MessageDeleted",
                        new { message.ConversationId, MessageId = message.Id });
                }
            }
        }

        if (targetUserId.HasValue)
        {
            var targetUser = await db.Users.SingleOrDefaultAsync(u => u.Id == targetUserId.Value, ct);
            if (targetUser != null)
            {
                if (request.FlagUser)
                    targetUser.IsFlagged = true;

                if (request.RestrictUserMinutes is > 0)
                    targetUser.RestrictedUntil = DateTime.UtcNow.AddMinutes(request.RestrictUserMinutes.Value);
            }
        }

        await db.SaveChangesAsync(ct);

        await auditService.LogAsync(null, moderatorId, "MODERATOR_DECISION", targetUserId, new
        {
            reportId,
            decision = request.Status.ToString(),
            request.ActionTaken,
            request.ModeratorNotes,
            request.FlagUser,
            request.RestrictUserMinutes,
            request.DeleteOffendingMessage
        }, ct);

        await tx.CommitAsync(ct);
        return await GetReportDetailsAsync(moderatorId, reportId, ct);
    }

    public async Task RestrictUserAsync(Guid moderatorId, Guid targetUserId, int minutes, string reason, CancellationToken ct = default)
    {
        await EnsureModeratorAsync(moderatorId, ct);
        if (minutes < 1 || minutes > 43200) throw AppException.BadRequest("Thời gian hạn chế từ 1 phút đến 30 ngày.");

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == targetUserId && u.DeletedAt == null, ct)
            ?? throw AppException.NotFound("Người dùng không tồn tại.");

        user.RestrictedUntil = DateTime.UtcNow.AddMinutes(minutes);
        await db.SaveChangesAsync(ct);

        await auditService.LogAsync(null, moderatorId, "RESTRICT_ACCOUNT", targetUserId, new
        {
            minutes,
            restrictedUntil = user.RestrictedUntil,
            reason
        }, ct);
    }

    public async Task UnrestrictUserAsync(Guid moderatorId, Guid targetUserId, CancellationToken ct = default)
    {
        await EnsureModeratorAsync(moderatorId, ct);

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == targetUserId && u.DeletedAt == null, ct)
            ?? throw AppException.NotFound("Người dùng không tồn tại.");

        user.RestrictedUntil = null;
        user.IsFlagged = false;
        await db.SaveChangesAsync(ct);

        await auditService.LogAsync(null, moderatorId, "UNRESTRICT_ACCOUNT", targetUserId, new
        {
            unrestrictedAt = DateTime.UtcNow
        }, ct);
    }

    private async Task EnsureModeratorAsync(Guid moderatorId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == moderatorId && u.IsActive && u.DeletedAt == null, ct)
            ?? throw AppException.Forbidden("Tài khoản không hợp lệ.");

        if (user.Role is not (UserRole.Moderator or UserRole.Admin))
            throw AppException.Forbidden("Chỉ kiểm duyệt viên hoặc quản trị viên mới có quyền thực hiện thao tác này.");
    }

    private async Task<string?> ResolveTargetPreviewAsync(ReportTargetType type, Guid targetId, CancellationToken ct)
    {
        if (type == ReportTargetType.User)
        {
            return await db.Users.AsNoTracking().Where(u => u.Id == targetId)
                .Select(u => $"{u.FullName} (@{u.Username})").SingleOrDefaultAsync(ct);
        }
        if (type == ReportTargetType.Message)
        {
            var raw = await db.Messages.AsNoTracking().Where(m => m.Id == targetId).Select(m => m.Content).SingleOrDefaultAsync(ct);
            if (raw == null) return "[Media/Voice]";
            var decrypted = _encryption.Decrypt(raw);
            return decrypted != null && decrypted.Length > 100 ? decrypted.Substring(0, 100) + "…" : decrypted ?? "[Media/Voice]";
        }
        if (type == ReportTargetType.Conversation)
        {
            return await db.Conversations.AsNoTracking().Where(c => c.Id == targetId)
                .Select(c => c.Name ?? "Hội thoại").SingleOrDefaultAsync(ct);
        }
        return null;
    }
}
