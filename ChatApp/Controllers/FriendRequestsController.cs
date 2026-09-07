using System.Security.Claims;
using ChatApp.DTOs;
using ChatApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Controllers;

[ApiController]
[Route("api/friend-requests")]
[Authorize]
public class FriendRequestsController : ControllerBase
{
    private readonly IFriendService _friendService;

    public FriendRequestsController(IFriendService friendService)
    {
        _friendService = friendService;
    }

    [HttpPost]
    public async Task<ActionResult<FriendRequestResponse>> Send(SendFriendRequestRequest request)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(rawUserId, out var senderId))
            return Unauthorized();

        return Ok(await _friendService.SendRequestAsync(senderId, request.ReceiverId));
    }

    [HttpGet("pending")]
    public async Task<ActionResult<List<PendingFriendRequestResponse>>> GetPending()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        
        Console.WriteLine($"Raw User ID: {rawUserId}"); // Debugging line

        if (!Guid.TryParse(rawUserId, out var receiverId))
            return Unauthorized();

        return Ok(await _friendService.GetPendingRequestsAsync(receiverId));
    }

    [HttpPut("{requestId:guid}/accept")]
    public async Task<ActionResult<FriendRequestResponse>> Accept(Guid requestId)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(rawUserId, out var receiverId))
            return Unauthorized();

        return Ok(await _friendService.AcceptRequestAsync(receiverId, requestId));
    }
}