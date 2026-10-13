using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChatApp.Common;
using ChatApp.Services;
using ChatApp.Data;
using ChatApp.Models;
using ChatApp.RateLimiting;
using ChatApp.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatApp.Calls;

public sealed class CallService(AppDbContext db, IRateLimitCounter quota, IOptions<CallOptions> options)
{
    private CallOptions Config => options.Value;
    public static bool IsLive(CallStatus status) => status is CallStatus.Ringing or CallStatus.Accepted;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void ValidateEndpoint(CallEndpoint? endpoint)
    {
        if (endpoint == null || endpoint.EndpointId == Guid.Empty || endpoint.Credential is not { Length: >= 32 and <= 128 })
            throw AppException.BadRequest("Thiết bị cuộc gọi không hợp lệ.");
    }
    private async Task Authenticate(CallActor actor, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (!await db.Users.AnyAsync(u => u.Id == actor.UserId && u.IsActive && u.DeletedAt == null, ct) ||
            !await SessionAccess.Active(db).AnyAsync(t => t.UserId == actor.UserId && t.FamilyId == actor.SessionId, ct))
            throw AppException.Unauthorized("Phiên đăng nhập đã kết thúc.");
    }
    private async Task Limit(CallActor actor, string scope, int limit, int seconds, CancellationToken ct)
    {
        if (!(await quota.AcquireAsync("call-" + scope, actor.UserId.ToString(), limit, TimeSpan.FromSeconds(seconds), ct)).Allowed)
            throw AppException.TooManyRequests("Bạn thao tác quá nhanh. Vui lòng thử lại sau.");
    }
    private Task<bool> Blocked(Guid a, Guid b, CancellationToken ct) => db.Blocks.AnyAsync(x =>
        (x.BlockerId == a && x.BlockedId == b) || (x.BlockerId == b && x.BlockedId == a), ct);
    private async Task LockUsers(IEnumerable<Guid> users, CancellationToken ct)
    {
        foreach (var id in users.Distinct().Order()) await ConversationLock.AcquireKeyAsync(db, "call-user:" + id, ct);
    }
    private async Task<(CallSession Call, List<CallParticipant> People)> Locked(Guid id, CancellationToken ct)
    {
        var conversation = await db.CallSessions.Where(c => c.Id == id).Select(c => c.ConversationId).SingleOrDefaultAsync(ct);
        if (conversation.HasValue) await ConversationLock.AcquireAsync(db, conversation.Value, ct);
        var people = await db.CallParticipants.Where(p => p.CallSessionId == id).ToListAsync(ct);
        await LockUsers(people.Where(p => p.UserId.HasValue).Select(p => p.UserId!.Value), ct);
        var call = await db.CallSessions.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw AppException.NotFound("Cuộc gọi không khả dụng.");
        return (call, people);
    }
    private static bool Owns(CallActor actor, CallParticipant p, CallEndpoint? endpoint, bool connection = true) =>
        endpoint?.Credential is { Length: >= 32 and <= 128 } && p.UserId == actor.UserId && p.AuthSessionId == actor.SessionId && p.EndpointId == endpoint.EndpointId &&
        p.CredentialHash == Hash(endpoint.Credential) && p.EndpointGeneration == endpoint.Generation &&
        (!connection || actor.ConnectionId == null || p.ConnectionId == actor.ConnectionId);
    private void Attach(CallActor actor, CallParticipant participant, CallEndpoint endpoint)
    {
        participant.EndpointId = endpoint.EndpointId; participant.AuthSessionId = actor.SessionId;
        participant.ConnectionId = actor.ConnectionId; participant.CredentialHash = Hash(endpoint.Credential);
        participant.EndpointGeneration = 1; participant.LastHeartbeatAt = DateTime.UtcNow;
        participant.LeaseExpiresAt = DateTime.UtcNow.AddSeconds(Config.LeaseSeconds);
    }
    private void Changed(CallSession call, IEnumerable<CallParticipant> people)
    {
        call.Version++;
        if (call.ConversationId is not Guid conversation) return;
        foreach (var p in people.Where(p => p.UserId.HasValue))
            ConversationEvents.Add(db, conversation, "CallStateChanged", new { eventId = Guid.NewGuid(), callId = call.Id, version = call.Version }, p.UserId);
    }
    public async Task<CallSnapshot> Start(CallActor actor, StartCall request, CancellationToken ct)
    {
        await Authenticate(actor, ct); ValidateEndpoint(request.Endpoint);
        if (request.ClientCallId == Guid.Empty || !Enum.IsDefined(request.Type) || actor.ConnectionId == null)
            throw AppException.BadRequest("Yêu cầu cuộc gọi không hợp lệ.");
        await Limit(actor, "start-request", 30, 60, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ConversationLock.AcquireKeyAsync(db, $"call-request:{actor.UserId}:{request.ClientCallId}", ct);
        var hash = Hash($"{request.ConversationId}:{request.Type}");
        var existing = await db.CallSessions.AsNoTracking().SingleOrDefaultAsync(c => c.CallerId == actor.UserId && c.ClientCallId == request.ClientCallId, ct);
        if (existing != null)
        {
            if (existing.RequestHash != hash) throw AppException.Conflict("Mã cuộc gọi đã dùng cho yêu cầu khác.");
            return await Snapshot(actor, existing.Id, request.Endpoint.EndpointId, ct);
        }
        await ConversationLock.AcquireAsync(db, request.ConversationId, ct);
        var direct = await db.DirectConversations.SingleOrDefaultAsync(d => d.ConversationId == request.ConversationId, ct);
        if (direct == null || (direct.UserLowId != actor.UserId && direct.UserHighId != actor.UserId))
            throw AppException.Forbidden("Không thể thực hiện cuộc gọi.");
        var peer = direct.UserLowId == actor.UserId ? direct.UserHighId : direct.UserLowId;
        await LockUsers([actor.UserId, peer], ct);
        if (!await db.Conversations.AnyAsync(c => c.Id == request.ConversationId && c.DeletedAt == null && c.ClosedAt == null, ct) ||
            await db.ConversationMembers.CountAsync(m => m.ConversationId == request.ConversationId && m.LeftAt == null &&
                (m.UserId == actor.UserId || m.UserId == peer), ct) != 2 ||
            !await db.Users.AnyAsync(u => u.Id == peer && u.IsActive && u.DeletedAt == null, ct) || await Blocked(actor.UserId, peer, ct))
            throw AppException.Forbidden("Không thể thực hiện cuộc gọi.");
        if (await db.ActiveCallSlots.AnyAsync(s => s.UserId == actor.UserId, ct)) throw AppException.Conflict("Bạn đang có cuộc gọi khác.");
        await Limit(actor, "create", 5, 60, ct);
        var busy = await db.ActiveCallSlots.AnyAsync(s => s.UserId == peer, ct);
        var call = new CallSession { ConversationId = request.ConversationId, CallerId = actor.UserId, Type = request.Type,
            ClientCallId = request.ClientCallId, RequestHash = hash, ExpiresAt = DateTime.UtcNow.AddSeconds(Config.RingSeconds) };
        var caller = new CallParticipant { CallSessionId = call.Id, UserId = actor.UserId, Status = CallParticipantStatus.Joined };
        var callee = new CallParticipant { CallSessionId = call.Id, UserId = peer, Status = CallParticipantStatus.Ringing };
        Attach(actor, caller, request.Endpoint);
        var people = new List<CallParticipant> { caller, callee };
        db.CallSessions.Add(call); db.CallParticipants.AddRange(people);
        if (busy) await Finish(call, people, CallStatus.Busy, "Busy", ct);
        else
        {
            db.ActiveCallSlots.AddRange(new ActiveCallSlot { UserId = actor.UserId, CallSessionId = call.Id }, new ActiveCallSlot { UserId = peer, CallSessionId = call.Id });
            Changed(call, people);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await Snapshot(actor, call.Id, request.Endpoint.EndpointId, ct);
    }
    public async Task<CallSnapshot?> Active(CallActor actor, Guid? endpoint, CancellationToken ct)
    {
        await Authenticate(actor, ct);
        var id = await db.ActiveCallSlots.Where(s => s.UserId == actor.UserId).Select(s => (Guid?)s.CallSessionId).SingleOrDefaultAsync(ct);
        return id.HasValue ? await Snapshot(actor, id.Value, endpoint, ct) : null;
    }
    public async Task<CallSnapshot?> ByClient(CallActor actor, Guid clientId, Guid? endpoint, CancellationToken ct)
    {
        await Authenticate(actor, ct);
        var id = await db.CallSessions.Where(c => c.CallerId == actor.UserId && c.ClientCallId == clientId).Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct);
        return id.HasValue ? await Snapshot(actor, id.Value, endpoint, ct) : null;
    }
    public async Task<CallSnapshot> Snapshot(CallActor actor, Guid id, Guid? endpoint, CancellationToken ct)
    {
        await Authenticate(actor, ct);
        var people = await db.CallParticipants.AsNoTracking().Where(p => p.CallSessionId == id).ToListAsync(ct);
        var me = people.SingleOrDefault(p => p.UserId == actor.UserId) ?? throw AppException.NotFound("Cuộc gọi không khả dụng.");
        var call = await db.CallSessions.AsNoTracking().SingleAsync(c => c.Id == id, ct);
        var peerId = people.FirstOrDefault(p => p.UserId != actor.UserId)?.UserId;
        var peer = await db.Users.AsNoTracking().Where(u => u.Id == peerId).Select(u => new { u.FullName, u.Username, u.AvatarUrl }).SingleOrDefaultAsync(ct);
        var mine = endpoint != null && me.EndpointId == endpoint && me.AuthSessionId == actor.SessionId;
        return new(call.Id, call.ConversationId, call.CallerId == actor.UserId ? call.ClientCallId : null,
            call.Type.ToString(), call.Status.ToString(), call.Version, call.CallerId == actor.UserId, mine, me.EndpointId != null,
            mine ? me.EndpointGeneration : 0, call.NegotiationEpoch, peer?.FullName ?? peer?.Username ?? "Người dùng", peer?.AvatarUrl,
            call.ExpiresAt, call.ConnectedAt, mine ? me.LeaseExpiresAt : null, call.RecoveryDeadlineAt,
            call.DurationSeconds ?? 0, call.EndedReason == "Blocked" ? "Ended" : call.EndedReason, DateTime.UtcNow);
    }
    public async Task<CallSnapshot> Command(CallActor actor, Guid id, CallCommand request, CancellationToken ct)
    {
        await Authenticate(actor, ct);
        await Limit(actor, request.Action == "heartbeat" ? "heartbeat" : "control", 60, 10, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (call, people) = await Locked(id, ct);
        var me = people.SingleOrDefault(p => p.UserId == actor.UserId) ?? throw AppException.Forbidden("Không có quyền.");
        var isCaller = call.CallerId == actor.UserId;
        if (request.Action is not ("accept" or "reject") && !Owns(actor, me, request.Endpoint, request.Action != "resume"))
            throw AppException.Forbidden("Cuộc gọi thuộc thiết bị khác.");
        if (request.Action is "accept" or "reject" && isCaller) throw AppException.Forbidden("Không có quyền.");
        if (await Expire(call, people, ct) || !IsLive(call.Status))
        { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await Snapshot(actor, id, request.Endpoint?.EndpointId, ct); }
        var now = DateTime.UtcNow;
        switch (request.Action)
        {
            case "accept":
                ValidateEndpoint(request.Endpoint);
                if (call.Status == CallStatus.Accepted)
                {
                    if (!Owns(actor, me, request.Endpoint)) throw AppException.Conflict("Cuộc gọi đã được nhận ở thiết bị khác.");
                    break;
                }
                if (actor.ConnectionId == null) throw AppException.BadRequest("Cần kết nối signaling.");
                var other = people.Single(p => p.UserId != actor.UserId);
                if (other.UserId == null || await Blocked(actor.UserId, other.UserId.Value, ct))
                { await Finish(call, people, CallStatus.Failed, "Blocked", ct); break; }
                Attach(actor, me, request.Endpoint!); me.Status = CallParticipantStatus.Joined; me.JoinedAt = now;
                call.Status = CallStatus.Accepted; call.AcceptedAt = now; call.ConnectDeadlineAt = now.AddSeconds(Config.ConnectSeconds);
                call.NegotiationEpoch = 1; Changed(call, people); break;
            case "reject":
                if (call.Status == CallStatus.Ringing) await Finish(call, people, CallStatus.Rejected, "Rejected", ct);
                break;
            case "cancel":
                if (!isCaller) throw AppException.Forbidden("Không có quyền.");
                await Finish(call, people, call.Status == CallStatus.Ringing ? CallStatus.Canceled : CallStatus.Ended, "Ended", ct); break;
            case "end":
                await Finish(call, people, call.Status == CallStatus.Ringing ? CallStatus.Canceled : CallStatus.Ended, "Ended", ct); break;
            case "heartbeat":
                me.LastHeartbeatAt = now; me.LeaseExpiresAt = now.AddSeconds(Config.LeaseSeconds); break;
            case "resume":
                if (actor.ConnectionId == null) throw AppException.BadRequest("Cần kết nối signaling.");
                // A retried resume on the same connection does not invalidate the caller's generation.
                if (me.ConnectionId != actor.ConnectionId) { me.ConnectionId = actor.ConnectionId; me.EndpointGeneration++; Changed(call, people); }
                me.LastHeartbeatAt = now; me.LeaseExpiresAt = now.AddSeconds(Config.LeaseSeconds); break;
            case "connected":
                if (call.Status != CallStatus.Accepted || request.Epoch != call.NegotiationEpoch) break;
                me.MediaConnectedAt ??= now;
                if (people.All(p => p.MediaConnectedAt != null) && (call.ConnectedAt == null || call.RecoveryDeadlineAt != null))
                { call.ConnectedAt ??= now; call.RecoveryDeadlineAt = null; call.RestartCount = 0; Changed(call, people); }
                break;
            case "restart":
                if (call.Status != CallStatus.Accepted || request.Epoch != call.NegotiationEpoch) break;
                if (call.RestartCount >= 2) { await Finish(call, people, CallStatus.Failed, "NetworkError", ct); break; }
                call.RestartCount++; call.NegotiationEpoch++; call.RecoveryDeadlineAt ??= now.AddSeconds(Config.RecoverySeconds);
                foreach (var p in people) p.MediaConnectedAt = null;
                Changed(call, people); break;
            case "failed":
                if (request.Epoch == call.NegotiationEpoch) await Finish(call, people, CallStatus.Failed, "NetworkError", ct);
                break;
            default: throw AppException.BadRequest("Thao tác không hợp lệ.");
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await Snapshot(actor, id, request.Endpoint?.EndpointId, ct);
    }
    public async Task Relay(CallActor actor, Guid id, CallSignal signal, Func<string, object, Task> deliver, CancellationToken ct)
    {
        await Authenticate(actor, ct); ValidateEndpoint(signal.Endpoint);
        if (signal.Type is not ("offer" or "answer" or "ice") || signal.SignalId == Guid.Empty || signal.Payload == null ||
            Encoding.UTF8.GetByteCount(signal.Payload) > (signal.Type == "ice" ? 4096 : 32768)) throw AppException.BadRequest("Tín hiệu không hợp lệ.");
        await Limit(actor, signal.Type == "ice" ? "ice" : "sdp", signal.Type == "ice" ? 200 : 12, 10, ct);
        using var parsed = JsonDocument.Parse(signal.Payload, new JsonDocumentOptions { MaxDepth = 8 });
        if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw AppException.BadRequest("Tín hiệu không hợp lệ.");
        if (signal.Type != "ice" && (!parsed.RootElement.TryGetProperty("type", out var kind) || kind.GetString() != signal.Type ||
            !parsed.RootElement.TryGetProperty("sdp", out var sdp) || sdp.ValueKind != JsonValueKind.String)) throw AppException.BadRequest("SDP không hợp lệ.");
        if (signal.Type == "ice" && (!parsed.RootElement.TryGetProperty("candidate", out var candidate) || candidate.ValueKind != JsonValueKind.String))
            throw AppException.BadRequest("ICE không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (call, people) = await Locked(id, ct);
        var me = people.SingleOrDefault(p => p.UserId == actor.UserId);
        if (me == null || !Owns(actor, me, signal.Endpoint) || call.Status != CallStatus.Accepted || signal.Epoch != call.NegotiationEpoch ||
            people.Any(p => p.LeaseExpiresAt <= DateTime.UtcNow) || (signal.Type == "offer" && actor.UserId != call.CallerId) ||
            (signal.Type == "answer" && actor.UserId == call.CallerId)) throw AppException.Forbidden("Không thể gửi tín hiệu.");
        var peer = people.Single(p => p.UserId != actor.UserId);
        if (peer.ConnectionId == null) throw AppException.Conflict("Đang chờ thiết bị kết nối lại.");
        await deliver(peer.ConnectionId, new { callId = id, epoch = signal.Epoch, signalId = signal.SignalId, type = signal.Type, payload = signal.Payload });
        await tx.CommitAsync(ct);
    }
    public async Task<object> Ice(CallActor actor, Guid id, CallEndpoint endpoint, CancellationToken ct)
    {
        await Authenticate(actor, ct); ValidateEndpoint(endpoint); await Limit(actor, "ice-config", 10, 60, ct);
        var p = await db.CallParticipants.AsNoTracking().SingleOrDefaultAsync(p => p.CallSessionId == id && p.UserId == actor.UserId, ct);
        var call = await db.CallSessions.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (p == null || call == null || !IsLive(call.Status) || !Owns(actor, p, endpoint, false) || p.LeaseExpiresAt <= DateTime.UtcNow)
            throw AppException.Forbidden("Không thể cấp cấu hình cuộc gọi.");
        var expires = DateTimeOffset.UtcNow.AddSeconds(Config.CredentialSeconds);
        var servers = new List<object>();
        if (Config.IceUrls.Length > 0) servers.Add(new { urls = Config.IceUrls });
        if (Config.TurnUrls.Length > 0)
        {
            if (Config.TurnSecret.Length < 32) throw AppException.Conflict("TURN chưa được cấu hình.");
            var username = $"{expires.ToUnixTimeSeconds()}:{actor.UserId}";
            var credential = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(Config.TurnSecret), Encoding.UTF8.GetBytes(username)));
            servers.Add(new { urls = Config.TurnUrls, username, credential });
        }
        return new { iceServers = servers, expiresAt = expires, relayAvailable = Config.TurnUrls.Length > 0 };
    }
    // Caller must already hold the conversation/user locks and transaction.
    public async Task Finish(CallSession call, List<CallParticipant> people, CallStatus status, string reason, CancellationToken ct)
    {
        if (!IsLive(call.Status)) return;
        var now = DateTime.UtcNow;
        call.Status = status; call.EndedAt = now; call.EndedReason = reason;
        call.DurationSeconds = call.ConnectedAt.HasValue ? Math.Max(0, (int)(now - call.ConnectedAt.Value).TotalSeconds) : 0;
        foreach (var p in people) { p.Status = status == CallStatus.Rejected ? CallParticipantStatus.Rejected : status == CallStatus.Missed ? CallParticipantStatus.Missed : CallParticipantStatus.Left; p.LeftAt = now; }
        await db.ActiveCallSlots.Where(s => s.CallSessionId == call.Id).ExecuteDeleteAsync(ct);
        if (call.ConversationId is Guid conversation && await db.Conversations.AnyAsync(c => c.Id == conversation && c.DeletedAt == null, ct) &&
            !await db.Messages.AnyAsync(m => m.CallSessionId == call.Id, ct))
        {
            var message = new Message { ConversationId = conversation, SenderId = call.CallerId, ClientMessageId = Guid.NewGuid(),
                CallSessionId = call.Id, Type = MessageType.CallLog, CreatedAt = now,
                Content = JsonSerializer.Serialize(new { schemaVersion = 1, callId = call.Id, callType = call.Type.ToString(), status = status.ToString(),
                    reason = reason == "Blocked" ? "Ended" : reason, durationSeconds = call.DurationSeconds }) };
            db.Messages.Add(message); await db.SaveChangesAsync(ct);
            await db.Conversations.Where(c => c.Id == conversation).ExecuteUpdateAsync(s => s.SetProperty(c => c.LastMessageId, (Guid?)message.Id)
                .SetProperty(c => c.LastMessageAt, (DateTime?)now).SetProperty(c => c.UpdatedAt, now), ct);
            await db.ConversationMembers.Where(m => m.ConversationId == conversation && m.LeftAt == null).ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.HiddenAt, (DateTime?)null).SetProperty(m => m.UnreadCount, m => m.UnreadCount + (m.UserId != call.CallerId && m.LastReadSequence < message.Sequence ? 1 : 0)), ct);
            ConversationEvents.Add(db, conversation, "MessageAvailable", new { conversationId = conversation, messageId = message.Id, sequence = message.Sequence });
        }
        Changed(call, people);
    }
    public async Task EndRingingForBlock(Guid conversation, CancellationToken ct = default)
    {
        var calls = await db.CallSessions.Where(c => c.ConversationId == conversation && c.Status == CallStatus.Ringing).ToListAsync(ct);
        foreach (var call in calls)
        {
            var people = await db.CallParticipants.Where(p => p.CallSessionId == call.Id).ToListAsync(ct);
            await LockUsers(people.Where(p => p.UserId.HasValue).Select(p => p.UserId!.Value), ct);
            await Finish(call, people, CallStatus.Failed, "Blocked", ct);
        }
    }
    private async Task<bool> Expire(CallSession call, List<CallParticipant> people, CancellationToken ct)
    {
        if (!IsLive(call.Status)) return false;
        var now = DateTime.UtcNow;
        string? reason = null;
        if (call.Status == CallStatus.Ringing && (call.ExpiresAt == null || call.ExpiresAt <= now)) reason = "Timeout";
        else if (call.Status == CallStatus.Accepted && ((call.ConnectedAt == null && (call.ConnectDeadlineAt == null || call.ConnectDeadlineAt <= now)) || call.RecoveryDeadlineAt <= now)) reason = "NetworkError";
        else if (people.Any(p => p.EndpointId != null && (p.LeaseExpiresAt == null || p.LeaseExpiresAt <= now))) reason = "NetworkError";
        foreach (var p in people.Where(p => p.EndpointId != null))
            if (!await SessionAccess.Active(db).AnyAsync(t => t.UserId == p.UserId && t.FamilyId == p.AuthSessionId, ct) ||
                !await db.Users.AnyAsync(u => u.Id == p.UserId && u.IsActive && u.DeletedAt == null, ct)) reason = "SessionEnded";
        if (reason == null) return false;
        await Finish(call, people, reason == "Timeout" ? CallStatus.Missed : CallStatus.Failed, reason, ct); return true;
    }
    public async Task Sweep(CancellationToken ct)
    {
        // Bounded keyset scan also detects revoked sessions, independent of heartbeat traffic.
        Guid? cursor = null;
        while (!ct.IsCancellationRequested)
        {
            var query = db.CallSessions.AsNoTracking().Where(c => c.Status == CallStatus.Ringing || c.Status == CallStatus.Accepted);
            if (cursor.HasValue) query = query.Where(c => c.Id.CompareTo(cursor.Value) > 0);
            var ids = await query.OrderBy(c => c.Id).Select(c => c.Id).Take(100).ToListAsync(ct);
            if (ids.Count == 0) break;
            foreach (var id in ids)
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var (call, people) = await Locked(id, ct);
                await Expire(call, people, ct); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
            cursor = ids[^1];
        }
    }
}


