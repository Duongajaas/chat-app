using System.Security.Claims;
using ChatApp.DTOs;
using ChatApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Controllers;

[ApiController, Authorize]
[Route("api/conversations")]
public class GroupsController(GroupService groups, IConversationService conversations) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> Members(Guid id) => Ok(await groups.MembersAsync(UserId, id));

    [HttpPatch("{id:guid}/group"), EnableRateLimiting("group-write")]
    public async Task<IActionResult> Rename(Guid id, RenameGroupRequest request)
    {
        await groups.RenameAsync(UserId, id, request.Name);
        return Ok(await conversations.GetConversationByIdAsync(UserId, id));
    }

    [HttpPost("{id:guid}/members"), EnableRateLimiting("group-write")]
    public async Task<IActionResult> Add(Guid id, AddGroupMemberRequest request)
    {
        await groups.AddAsync(UserId, id, request.UserId);
        return NoContent();
    }

    [HttpDelete("{id:guid}/members/{target:guid}"), EnableRateLimiting("group-write")]
    public async Task<IActionResult> Kick(Guid id, Guid target)
    {
        await groups.RemoveAsync(UserId, id, target, true);
        return NoContent();
    }

    [HttpPut("{id:guid}/members/{target:guid}/role"), EnableRateLimiting("group-write")]
    public async Task<IActionResult> Role(Guid id, Guid target, ChangeGroupRoleRequest request)
    {
        await groups.SetRoleAsync(UserId, id, target, request.Role);
        return NoContent();
    }

    [HttpGet("{id:guid}/invites")]
    public async Task<IActionResult> Invites(Guid id) => Ok(await groups.InvitesAsync(UserId, id));

    [HttpPost("{id:guid}/invites"), EnableRateLimiting("group-write")]
    public async Task<IActionResult> CreateInvite(Guid id, CreateGroupInviteRequest request, [FromHeader(Name = "Idempotency-Key")] Guid? key)
    {
        if (!key.HasValue || key == Guid.Empty) return BadRequest(new { message = "Idempotency-Key UUID là bắt buộc." });
        return Ok(await groups.CreateInviteAsync(UserId, id, request, key));
    }

    [HttpDelete("{id:guid}/invites/{invite:guid}"), EnableRateLimiting("group-write")]
    public async Task<IActionResult> Revoke(Guid id, Guid invite)
    {
        await groups.RevokeAsync(UserId, id, invite);
        return NoContent();
    }

    [HttpPost("join"), EnableRateLimiting("group-join")]
    public async Task<IActionResult> Join(JoinGroupRequest request)
    {
        var id = await groups.JoinAsync(UserId, request.Code);
        return Ok(await conversations.GetConversationByIdAsync(UserId, id));
    }
}
