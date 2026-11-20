using System.ComponentModel.DataAnnotations;
using ChatApp.Models;

namespace ChatApp.DTOs;

public record CreateReportRequest(
    [Required] ReportTargetType TargetType,
    [Required] Guid TargetId,
    [Required, StringLength(100, MinimumLength = 3)] string Reason,
    [StringLength(1000)] string? Description
);

public record ReportResponse(
    Guid Id,
    Guid ReporterId,
    string? ReporterUsername,
    string? ReporterFullName,
    ReportTargetType TargetType,
    Guid TargetId,
    string? TargetPreview,
    string Reason,
    string? Description,
    ReportStatus Status,
    Guid? ReviewedBy,
    string? ReviewedByUsername,
    DateTime? ReviewedAt,
    DateTime CreatedAt
);

public record ReportSummaryResponse(
    Guid Id,
    Guid ReporterId,
    string? ReporterUsername,
    ReportTargetType TargetType,
    Guid TargetId,
    string Reason,
    ReportStatus Status,
    DateTime CreatedAt
);

public record ReportFilterRequest(
    ReportStatus? Status = null,
    ReportTargetType? TargetType = null,
    int Page = 1,
    int PageSize = 20
);

public record UpdateReportStatusRequest(
    [Required] ReportStatus Status
);

public record ModerationDecisionRequest(
    [Required] ReportStatus Status,
    string? ActionTaken = null,
    string? ModeratorNotes = null,
    bool FlagUser = false,
    int? RestrictUserMinutes = null,
    bool DeleteOffendingMessage = false
);

public record UserRestrictionRequest(
    [Required, Range(1, 43200)] int RestrictMinutes,
    [Required, StringLength(500)] string Reason
);
