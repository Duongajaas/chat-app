using System.Security.Claims;
using ChatApp.Common;
using ChatApp.DTOs;
using ChatApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Controllers;

[ApiController]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly IMessageService _messageService;

    public MessagesController(IMessageService messageService)
    {
        _messageService = messageService;
    }

    [HttpGet("api/conversations/{conversationId:guid}/messages")]
    [EnableRateLimiting(RateLimitPolicies.GetMessages)]
    public async Task<ActionResult<MessageListResponse>> GetMessages(
        Guid conversationId,
        [FromQuery] long? before = null,
        [FromQuery] long? after = null,
        [FromQuery] int limit = 50)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(rawUserId, out var userId))
            return Unauthorized();

        return Ok(await _messageService.GetMessagesAsync(userId, conversationId, before, after, limit));
    }

    [HttpPost("api/conversations/{conversationId:guid}/messages")]
    public async Task<ActionResult<MessageResponse>> Send(Guid conversationId, [FromBody] SendMessagePayload payload)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(rawUserId, out var userId))
            return Unauthorized();

        var clientMessageId = payload.ClientMessageId ?? Guid.NewGuid();
        var request = new SendMessageRequest(payload.Content);
        return Ok(await _messageService.SendMessageAsync(userId, clientMessageId, conversationId, request));
    }

    [HttpDelete("api/messages/{messageId:guid}")]
    public async Task<IActionResult> Delete(Guid messageId)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(rawUserId, out var userId))
            return Unauthorized();

        await _messageService.DeleteMessageAsync(userId, messageId);
        return NoContent();
    }
}