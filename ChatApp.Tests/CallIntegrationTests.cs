using ChatApp.Calls;
using ChatApp.Common;
using ChatApp.Data;
using ChatApp.Models;
using ChatApp.RateLimiting;
using ChatApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChatApp.Tests;
public sealed class CallPostgresFactAttribute : FactAttribute
{
    public CallPostgresFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHATAPP_TEST_POSTGRES"))) Skip = "Requires isolated CHATAPP_TEST_POSTGRES"; }
}
public class CallIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private readonly InMemoryRateLimitCounter counter = new(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
    private CallService Service(AppDbContext db) => new(db, counter, Microsoft.Extensions.Options.Options.Create(new CallOptions()));
    private static CallEndpoint Endpoint() => new(Guid.NewGuid(), new string('a', 48));
    private async Task<(Guid Room, CallActor A, CallActor B)> Seed()
    {
        await using var db = fixture.Open();
        var a = new User { Username = Guid.NewGuid().ToString("N"), FullName = "A" };
        var b = new User { Username = Guid.NewGuid().ToString("N"), FullName = "B" };
        var fa = Guid.NewGuid(); var fb = Guid.NewGuid();
        var room = new Conversation { Type = ConversationType.Direct };
        db.Users.AddRange(a, b); db.Conversations.Add(room);
        var ids = new[] { a.Id, b.Id }.Order().ToArray();
        db.DirectConversations.Add(new DirectConversation { ConversationId = room.Id, UserLowId = ids[0], UserHighId = ids[1] });
        db.ConversationMembers.AddRange(new ConversationMember { ConversationId = room.Id, UserId = a.Id }, new ConversationMember { ConversationId = room.Id, UserId = b.Id });
        db.RefreshTokens.AddRange(new RefreshToken { UserId = a.Id, FamilyId = fa, TokenHash = Guid.NewGuid().ToString(), ExpiresAt = DateTime.UtcNow.AddDays(1) },
            new RefreshToken { UserId = b.Id, FamilyId = fb, TokenHash = Guid.NewGuid().ToString(), ExpiresAt = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync(); return (room.Id, new(a.Id, fa, "a-connection"), new(b.Id, fb, "b-connection"));
    }
    private async Task<T> With<T>(Func<CallService, Task<T>> run) { await using var db = fixture.Open(); return await run(Service(db)); }
    [CallPostgresFact]
    public async Task Concurrent_retry_creates_one_call_and_terminal_command_creates_one_log()
    {
        var (room, a, _) = await Seed(); var endpoint = Endpoint(); var request = new StartCall(room, CallType.Audio, Guid.NewGuid(), endpoint);
        var calls = await Task.WhenAll(With(s => s.Start(a, request, default)), With(s => s.Start(a, request, default)));
        Assert.Equal(calls[0].Id, calls[1].Id);
        await Task.WhenAll(With(s => s.Command(a, calls[0].Id, new("end", endpoint), default)), With(s => s.Command(a, calls[0].Id, new("end", endpoint), default)));
        await using var db = fixture.Open();
        Assert.Equal(1, await db.Messages.CountAsync(m => m.CallSessionId == calls[0].Id));
        Assert.False(await db.ActiveCallSlots.AnyAsync(s => s.CallSessionId == calls[0].Id));
        Assert.Equal(1, await db.ConversationMembers.Where(m => m.ConversationId == room && m.UserId != a.UserId).Select(m => m.UnreadCount).SingleAsync());
    }
    [CallPostgresFact]
    public async Task Two_callee_devices_cannot_both_control_signaling()
    {
        var (room, a, b) = await Seed(); var ca = Endpoint(); var cb = Endpoint(); var other = Endpoint();
        var call = await With(s => s.Start(a, new(room, CallType.Audio, Guid.NewGuid(), ca), default));
        await With(s => s.Command(b, call.Id, new("accept", cb), default));
        var loser = b with { ConnectionId = "other-tab" };
        await Assert.ThrowsAsync<AppException>(() => With(s => s.Command(loser, call.Id, new("accept", other), default)));
        await Assert.ThrowsAsync<AppException>(() => With(s => s.Command(loser, call.Id, new("end", other), default)));
        var accepted = await With(s => s.Snapshot(b, call.Id, cb.EndpointId, default));
        Assert.True(accepted.OwnsEndpoint); Assert.Equal("Accepted", accepted.Status);
    }
    [CallPostgresFact]
    public async Task Block_ends_ringing_but_preserves_accepted_call()
    {
        var (room, a, b) = await Seed(); var ca = Endpoint();
        var call = await With(s => s.Start(a, new(room, CallType.Audio, Guid.NewGuid(), ca), default));
        await using (var db = fixture.Open()) await new BlockService(db, Service(db)).BlockUserAsync(b.UserId, a.UserId);
        Assert.Equal("Failed", (await With(s => s.Snapshot(a, call.Id, ca.EndpointId, default))).Status);
        var (room2, a2, b2) = await Seed(); var ep = Endpoint(); var eb = Endpoint();
        var connected = await With(s => s.Start(a2, new(room2, CallType.Video, Guid.NewGuid(), ep), default));
        await With(s => s.Command(b2, connected.Id, new("accept", eb), default));
        await using (var db = fixture.Open()) await new BlockService(db, Service(db)).BlockUserAsync(b2.UserId, a2.UserId);
        Assert.Equal("Accepted", (await With(s => s.Command(a2, connected.Id, new("heartbeat", ep), default))).Status);
    }
    [CallPostgresFact]
    public async Task One_dead_endpoint_expires_even_when_peer_heartbeats()
    {
        var (room, a, b) = await Seed(); var ca = Endpoint(); var cb = Endpoint();
        var call = await With(s => s.Start(a, new(room, CallType.Audio, Guid.NewGuid(), ca), default));
        await With(s => s.Command(b, call.Id, new("accept", cb), default));
        await using (var db = fixture.Open()) await db.CallParticipants.Where(p => p.CallSessionId == call.Id && p.UserId == b.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LeaseExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        Assert.Equal("Failed", (await With(s => s.Command(a, call.Id, new("heartbeat", ca), default))).Status);
    }
    [CallPostgresFact]
    public async Task Accept_after_deadline_cannot_beat_delayed_sweeper()
    {
        var (room, a, b) = await Seed(); var call = await With(s => s.Start(a, new(room, CallType.Audio, Guid.NewGuid(), Endpoint()), default));
        await using (var db = fixture.Open()) await db.CallSessions.Where(c => c.Id == call.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        Assert.Equal("Missed", (await With(s => s.Command(b, call.Id, new("accept", Endpoint()), default))).Status);
    }
}


