using System.Security.Claims;
using ChatApp.Data;
using ChatApp.Common;
using ChatApp.DTOs;
using ChatApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Controllers;

[ApiController, Authorize, Route("api/conversations/{id:guid}")]
public class MessageFeaturesController(AppDbContext db) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpPost("messages/reconcile"), EnableRateLimiting(RateLimitPolicies.GetMessages)]
    public async Task<IActionResult> Reconcile(Guid id, ReconcileRequest request) =>
        Ok(await new MessageReconciliation(db).ReconcileAsync(UserId, id, request.Items, HttpContext.RequestAborted));
    [HttpPost("messages/batch"), EnableRateLimiting(RateLimitPolicies.GetMessages)]
    public async Task<IActionResult> Batch(Guid id, MessageBatchRequest request) =>
        Ok(await new MessageReader(db).ReadAsync(UserId, id, request.Ids, HttpContext.RequestAborted));
    [HttpGet("pins"), EnableRateLimiting(RateLimitPolicies.GetMessages)]
    public async Task<IActionResult> Pins(Guid id) => Ok(await new PinService(db).ListAsync(UserId, id));
    [HttpPut("pins/{messageId:guid}"), EnableRateLimiting("group-write")]
    public async Task<IActionResult> Pin(Guid id, Guid messageId)
    {
        await new PinService(db).SetAsync(UserId, id, messageId, true); return NoContent();
    }
    [HttpDelete("pins/{messageId:guid}"), EnableRateLimiting("group-write")]
    public async Task<IActionResult> Unpin(Guid id, Guid messageId)
    {
        await new PinService(db).SetAsync(UserId, id, messageId, false); return NoContent();
    }
    [HttpGet("mentions"), EnableRateLimiting(RateLimitPolicies.GetMessages)]
    public async Task<IActionResult> Mentions(Guid id)
    {
        var reader = new MessageReader(db); await reader.EnsureMembershipAsync(UserId, id);
        var ids = await reader.Visible(UserId, id).Where(m => m.DeletedAt == null &&
            db.MessageMentions.Any(t => t.MessageId == m.Id && t.MentionedUserId == UserId))
            .OrderByDescending(m => m.Sequence).Take(50).Select(m => m.Id).ToArrayAsync();
        return Ok(await reader.ReadAsync(UserId, id, ids));
    }
}
