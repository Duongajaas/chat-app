using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ChatApp.DTOs;
using ChatApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Controllers;

[ApiController]
[Route("api/conversations")]
[Authorize]
public class ConversationsController : ControllerBase
{
    private readonly IConversationService _conversationService;

    public ConversationsController(IConversationService conversationService)
    {
        _conversationService = conversationService;
    }

    [HttpGet]
    public async Task<ActionResult<ConversationPageResponse>> GetMine([FromQuery] string? cursor = null, [FromQuery] int limit = 30)
    {
        return Ok(await _conversationService.GetMyConversationsAsync(CurrentUserId, cursor, limit));
    }

    [HttpGet("{conversationId:guid}")]
    public async Task<ActionResult<ConversationSummaryResponse>> GetById(Guid conversationId)
    {
        return Ok(await _conversationService.GetConversationByIdAsync(CurrentUserId, conversationId));
    }

    [HttpPost("direct")]
    public async Task<ActionResult<ConversationSummaryResponse>> CreateDirect(CreateDirectConversationRequest request)
    {
        return Ok(await _conversationService.GetOrCreateDirectConversationAsync(CurrentUserId, request.OtherUserId));
    }

    [HttpPost("group"), EnableRateLimiting("group-write")]
    public async Task<ActionResult<ConversationSummaryResponse>> CreateGroup(CreateConversationRequest request, [FromHeader(Name = "Idempotency-Key")] Guid? key)
    {
        if (request.Type != Models.ConversationType.Group)
            return BadRequest(new { message = "Conversation group phải có type là Group." });

        if (!key.HasValue || key == Guid.Empty) return BadRequest(new { message = "Idempotency-Key UUID là bắt buộc." });
        return Ok(await _conversationService.CreateConversationAsync(CurrentUserId, request, key));
    }

    [HttpPost("{conversationId:guid}/hide")]
    [HttpDelete("{conversationId:guid}")]
    public async Task<IActionResult> Delete(Guid conversationId)
    {
        await _conversationService.DeleteConversationAsync(CurrentUserId, conversationId);
        return NoContent();
    }

    [HttpPost("{conversationId:guid}/leave"), EnableRateLimiting("group-write")]
    public async Task<IActionResult> Leave(Guid conversationId)
    {
        await _conversationService.LeaveConversationAsync(CurrentUserId, conversationId);
        return NoContent();
    }

    private Guid CurrentUserId
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return Guid.TryParse(raw, out var userId) ? userId : throw new UnauthorizedAccessException();
        }
    }
}

public record CreateDirectConversationRequest(
    [Required] Guid OtherUserId
);