using System.Security.Claims;
using ChatApp.Common;
using ChatApp.DTOs;
using ChatApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Controllers;

[ApiController]
[Route("api/friend-links")]
[Authorize]
public class FriendLinksController : ControllerBase
{
    private readonly IFriendLinkService _friendLinkService;

    public FriendLinksController(IFriendLinkService friendLinkService)
    {
        _friendLinkService = friendLinkService;
    }

    [HttpGet("me")]
    public async Task<ActionResult<FriendLinkResponse>> GetMine()
    {
        return Ok(await _friendLinkService.GetOrCreateActiveLinkAsync(CurrentUserId));
    }

    [HttpPost("regenerate")]
    public async Task<ActionResult<FriendLinkResponse>> Regenerate()
    {
        return Ok(await _friendLinkService.RegenerateLinkAsync(CurrentUserId));
    }

    [HttpDelete("{linkId:guid}")]
    public async Task<IActionResult> Revoke(Guid linkId)
    {
        Console.WriteLine($"Revoke link called for user {CurrentUserId} and link {linkId}");
        await _friendLinkService.RevokeLinkAsync(CurrentUserId, linkId);
        return NoContent();
    }

    [HttpGet("resolve/{token}")]
    [EnableRateLimiting(RateLimitPolicies.ResolveFriendLink)]
    public async Task<ActionResult<PublicUserProfileResponse>> Resolve(string token)
    {
        return Ok(await _friendLinkService.ResolveLinkAsync(token, CurrentUserId));
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