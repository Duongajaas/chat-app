using ChatApp.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Calls;

[ApiController, Authorize, Route("api/calls")]
public sealed class CallsController(CallService calls) : ControllerBase
{
    private CallActor Actor => CallActor.From(User);
    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await action()); }
        catch (AppException ex) { return StatusCode(ex.StatusCode, new { message = ex.Message }); }
    }
    [HttpGet("active")]
    public Task<IActionResult> Active(Guid? endpointId, CancellationToken ct) => Run(async () => await calls.Active(Actor, endpointId, ct));
    [HttpGet("by-client-id/{clientId:guid}")]
    public Task<IActionResult> ByClient(Guid clientId, Guid? endpointId, CancellationToken ct) => Run(async () => await calls.ByClient(Actor, clientId, endpointId, ct));
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Snapshot(Guid id, Guid? endpointId, CancellationToken ct) => Run(async () => await calls.Snapshot(Actor, id, endpointId, ct));
    [HttpPost("{id:guid}/heartbeat")]
    public Task<IActionResult> Heartbeat(Guid id, CallEndpoint endpoint, CancellationToken ct) => Run(async () => await calls.Command(Actor, id, new("heartbeat", endpoint), ct));
    [HttpPost("{id:guid}/end")]
    public Task<IActionResult> End(Guid id, CallEndpoint endpoint, CancellationToken ct) => Run(async () => await calls.Command(Actor, id, new("end", endpoint), ct));
    // Credential travels in a body, never URL/history/access logs.
    [HttpPost("{id:guid}/ice-servers")]
    public Task<IActionResult> Ice(Guid id, CallEndpoint endpoint, CancellationToken ct) => Run(() => calls.Ice(Actor, id, endpoint, ct)!);
}
