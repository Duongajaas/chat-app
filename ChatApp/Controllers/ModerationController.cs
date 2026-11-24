using System.Security.Claims;
using ChatApp.Common;
using ChatApp.DTOs;
using ChatApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Controllers;

[ApiController]
[Route("api/moderation")]
[Authorize(Policy = "RequireModerator")]
public class ModerationController(IReportService reports, IAuditService auditService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("reports")]
    [EnableRateLimiting(RateLimitPolicies.GetMessages)]
    public async Task<ActionResult<List<ReportSummaryResponse>>> GetReports([FromQuery] ReportFilterRequest filter)
    {
        return Ok(await reports.GetReportsAsync(UserId, filter, HttpContext.RequestAborted));
    }

    [HttpGet("reports/{id:guid}")]
    [EnableRateLimiting(RateLimitPolicies.GetMessages)]
    public async Task<ActionResult<ReportResponse>> GetReportDetails(Guid id)
    {
        return Ok(await reports.GetReportDetailsAsync(UserId, id, HttpContext.RequestAborted));
    }

    [HttpPatch("reports/{id:guid}/status")]
    public async Task<ActionResult<ReportResponse>> UpdateStatus(Guid id, [FromBody] UpdateReportStatusRequest request)
    {
        return Ok(await reports.UpdateStatusAsync(UserId, id, request.Status, HttpContext.RequestAborted));
    }

    [HttpPost("reports/{id:guid}/decision")]
    public async Task<ActionResult<ReportResponse>> ApplyDecision(Guid id, [FromBody] ModerationDecisionRequest request)
    {
        return Ok(await reports.ApplyDecisionAsync(UserId, id, request, HttpContext.RequestAborted));
    }

    [HttpPost("users/{userId:guid}/restrictions")]
    public async Task<IActionResult> RestrictUser(Guid userId, [FromBody] UserRestrictionRequest request)
    {
        await reports.RestrictUserAsync(UserId, userId, request.RestrictMinutes, request.Reason, HttpContext.RequestAborted);
        return NoContent();
    }

    [HttpDelete("users/{userId:guid}/restrictions")]
    public async Task<IActionResult> UnrestrictUser(Guid userId)
    {
        await reports.UnrestrictUserAsync(UserId, userId, HttpContext.RequestAborted);
        return NoContent();
    }

    [HttpGet("audit-logs")]
    [EnableRateLimiting(RateLimitPolicies.GetMessages)]
    public async Task<ActionResult<List<AuditLogResponse>>> GetAuditLogs([FromQuery] int limit = 50)
    {
        return Ok(await auditService.GetModerationLogsAsync(UserId, limit, HttpContext.RequestAborted));
    }
}
