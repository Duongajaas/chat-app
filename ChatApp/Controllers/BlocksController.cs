using System.Security.Claims;
using ChatApp.DTOs;
using ChatApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Controllers;

[ApiController]
[Route("api/blocks")]
[Authorize]
public class BlocksController : ControllerBase
{
    private readonly IBlockService _blockService;

    public BlocksController(IBlockService blockService)
    {
        _blockService = blockService;
    }

    [HttpGet]
    public async Task<ActionResult<List<BlockedUserResponse>>> GetBlockedUsers()
    {
        return Ok(await _blockService.GetBlockedUsersAsync(CurrentUserId));
    }

    [HttpPost("{userId:guid}")]
    public async Task<IActionResult> BlockUser(Guid userId)
    {
        await _blockService.BlockUserAsync(CurrentUserId, userId);
        return NoContent();
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> UnblockUser(Guid userId)
    {
        await _blockService.UnblockUserAsync(CurrentUserId, userId);
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
