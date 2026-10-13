using System.Security.Claims;
using ChatApp.Common;
using ChatApp.Models;

namespace ChatApp.Calls;

public record CallActor(Guid UserId, Guid SessionId, string? ConnectionId)
{
    public static CallActor From(ClaimsPrincipal user, string? connection = null)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ||
            !Guid.TryParse(user.FindFirstValue("sid"), out var session))
            throw AppException.Unauthorized("Vui lòng đăng nhập lại để gọi.");
        return new(id, session, connection);
    }
}
public record CallEndpoint(Guid EndpointId, string Credential, long Generation = 1);
public record StartCall(Guid ConversationId, CallType Type, Guid ClientCallId, CallEndpoint Endpoint);
public record CallCommand(string Action, CallEndpoint? Endpoint = null, int Epoch = 0);
public record CallSignal(int Epoch, Guid SignalId, string Type, string Payload, CallEndpoint Endpoint);
public record CallSnapshot(Guid Id, Guid? ConversationId, Guid? ClientCallId, string Type, string Status,
    long Version, bool IsCaller, bool OwnsEndpoint, bool HasEndpoint, long Generation, int Epoch,
    string PeerName, string? PeerAvatar, DateTime? ExpiresAt, DateTime? ConnectedAt,
    DateTime? LeaseExpiresAt, DateTime? RecoveryDeadlineAt, int DurationSeconds, string? Reason, DateTime ServerTime);
