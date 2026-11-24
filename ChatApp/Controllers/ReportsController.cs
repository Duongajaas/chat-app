using System.Security.Claims;
using ChatApp.Common;
using ChatApp.DTOs;
using ChatApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController(IReportService reports) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Report)]
    public async Task<ActionResult<ReportResponse>> Submit([FromBody] CreateReportRequest request)
    {
        var result = await reports.SubmitReportAsync(UserId, request, HttpContext.RequestAborted);
        return Ok(result);
    }
}
