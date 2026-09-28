using System.Security.Claims;
using ChatApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Controllers;

public record ChangeAvatarRequest(Guid AssetId, long ExpectedVersion);
[ApiController, Authorize, Route("api/conversations/{id:guid}/avatar"), EnableRateLimiting("group-write")]
public class AvatarsController(AvatarService avatars, IConversationService conversations) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpPost("upload-intent")]
    public async Task<IActionResult> Prepare(Guid id) => Ok(await avatars.PrepareAsync(UserId, id, HttpContext.RequestAborted));
    [HttpPut]
    public async Task<IActionResult> Apply(Guid id, ChangeAvatarRequest request)
    {
        await avatars.ApplyAsync(UserId, id, request.AssetId, request.ExpectedVersion, HttpContext.RequestAborted);
        return Ok(await conversations.GetConversationByIdAsync(UserId, id));
    }
}
