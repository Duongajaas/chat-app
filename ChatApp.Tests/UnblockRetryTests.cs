using ChatApp.Common;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChatApp.Tests;

public class UnblockRetryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private async Task<(Guid A, Guid B, Guid Id)> Seed(bool group = false)
    {
        await using var db = fixture.Open();
        var a = new User { Username = Guid.NewGuid().ToString("N"), FullName = "A" };
        var b = new User { Username = Guid.NewGuid().ToString("N"), FullName = "B" };
        db.Users.AddRange(a, b);
        var pair = new[] { a.Id, b.Id }.Order().ToArray();
        db.Friendships.Add(new Friendship { UserLowId = pair[0], UserHighId = pair[1] });
        await db.SaveChangesAsync();
        var service = new ConversationService(db);
        var conversation = group ? await service.CreateConversationAsync(a.Id, new(ConversationType.Group, "Retry", [b.Id]))
            : await service.GetOrCreateDirectConversationAsync(a.Id, b.Id);
        return (a.Id, b.Id, conversation.Id);
    }

    [Fact]
    public async Task Unblock_is_idempotent_and_cooldown_is_directional_and_atomic()
    {
        var (a, b, id) = await Seed();
        await using var db = fixture.Open(); var blocks = new BlockService(db);
        await blocks.BlockUserAsync(a, b); await blocks.BlockUserAsync(b, a);
        await blocks.UnblockUserAsync(a, b);
        var cooldown = await db.BlockCooldowns.AsNoTracking().SingleAsync(c => c.ActorId == a && c.TargetId == b);
        Assert.Equal(TimeSpan.FromHours(1), cooldown.ReblockAllowedAt - cooldown.LastUnblockedAt);
        var events = await db.OutboxEvents.CountAsync();
        await blocks.UnblockUserAsync(a, b);
        Assert.Equal(events, await db.OutboxEvents.CountAsync());
        Assert.Equal(cooldown.ReblockAllowedAt, (await db.BlockCooldowns.AsNoTracking().SingleAsync(c => c.ActorId == a)).ReblockAllowedAt);
        Assert.True(await blocks.IsBlockedEitherWayAsync(a, b));
        Assert.True((await new ConversationService(db).GetConversationByIdAsync(a, id)).IsBlocked);
        var error = await Assert.ThrowsAsync<AppException>(() => blocks.BlockUserAsync(a, b));
        Assert.Equal(409, error.StatusCode); Assert.Equal("REBLOCK_COOLDOWN", error.Code);
        Assert.InRange(error.RemainingSeconds!.Value, 3500, 3600);
        Assert.Equal(events, await db.OutboxEvents.CountAsync());
        Assert.False(await db.Blocks.AnyAsync(x => x.BlockerId == a && x.BlockedId == b));
        Assert.False(await db.Friendships.AnyAsync(f => f.UserLowId == a || f.UserHighId == a));
        var payloads = await db.OutboxEvents.Where(e => e.ConversationId == id && e.EventName == "ConversationChanged").Select(e => e.Payload).ToListAsync();
        Assert.Equal(3, payloads.Count);
        Assert.All(payloads, p => { Assert.DoesNotContain("isBlocked", p); Assert.DoesNotContain("actorId", p); });
    }

    [Fact]
    public async Task Expired_cooldown_allows_block_and_concurrent_unblock_does_not_restart_it()
    {
        var (a, b, _) = await Seed();
        await using (var db = fixture.Open()) await new BlockService(db).BlockUserAsync(a, b);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ => { await using var db = fixture.Open(); await new BlockService(db).UnblockUserAsync(a, b); }));
        await using var verify = fixture.Open();
        Assert.Single(await verify.BlockCooldowns.Where(c => c.ActorId == a).ToListAsync());
        await Assert.ThrowsAsync<AppException>(() => new BlockService(verify).BlockUserAsync(a, b));
        await verify.BlockCooldowns.Where(c => c.ActorId == a).ExecuteUpdateAsync(s => s.SetProperty(c => c.ReblockAllowedAt, DateTime.UtcNow.AddSeconds(-1)));
        await new BlockService(verify).BlockUserAsync(a, b);
        Assert.True(await verify.Blocks.AnyAsync(c => c.BlockerId == a && c.BlockedId == b));
    }

    [Theory]
    [InlineData("block", "Readable")]
    [InlineData("kick", "Readable")]
    [InlineData("hide", "Hidden")]
    [InlineData("recall", "Recalled")]
    public async Task Reconcile_and_retry_confirm_committed_message_after_permissions_or_state_change(string action, string expected)
    {
        var (a, b, id) = await Seed(action == "kick"); var key = Guid.NewGuid();
        await using var db = fixture.Open(); var messages = new MessageService(db, new BlockService(db));
        var first = await messages.SendMessageAsync(b, key, id, new("original"));
        if (action == "block") await new BlockService(db).BlockUserAsync(a, b);
        if (action == "kick") await new GroupService(db).RemoveAsync(a, id, b, true);
        if (action == "hide") await messages.DeleteForMeAsync(b, first.Id);
        if (action == "recall") await messages.DeleteMessageAsync(b, first.Id);
        var count = await db.OutboxEvents.CountAsync();
        var unread = (await db.ConversationMembers.AsNoTracking().SingleAsync(m => m.UserId == a && m.ConversationId == id)).UnreadCount;
        var receipt = Assert.Single(await new MessageReconciliation(db).ReconcileAsync(b, id, [new(key, "original")], default));
        Assert.Equal(expected, receipt.State);
        var retry = await messages.SendMessageAsync(b, key, id, new("original"));
        Assert.Equal(first.Id, retry.Id);
        if (action == "hide") { Assert.Null(receipt.Message); Assert.Null(retry.Content); Assert.Equal("Hidden", retry.Status); }
        if (action == "recall") { Assert.Equal("Deleted", retry.Status); Assert.NotEqual("original", retry.Content); }
        Assert.Equal(count, await db.OutboxEvents.CountAsync());
        Assert.Equal(1, await db.Messages.CountAsync(m => m.ConversationId == id));
        Assert.Equal(unread, (await db.ConversationMembers.AsNoTracking().SingleAsync(m => m.UserId == a && m.ConversationId == id)).UnreadCount);
    }

    [Fact]
    public async Task Reconciliation_is_scoped_to_sender_and_detects_different_payload()
    {
        var (a, b, id) = await Seed(); var key = Guid.NewGuid();
        await using var db = fixture.Open();
        await new MessageService(db, new BlockService(db)).SendMessageAsync(a, key, id, new("private"));
        var service = new MessageReconciliation(db);
        Assert.Equal("NotFound", (await service.ReconcileAsync(b, id, [new(key, "private")], default)).Single().State);
        Assert.Equal("Conflict", (await service.ReconcileAsync(a, id, [new(key, "changed")], default)).Single().State);
        Assert.Equal("Conflict", (await service.ReconcileAsync(a, Guid.NewGuid(), [new(key, "private")], default)).Single().State);
    }

    [Fact]
    public async Task Reconcile_unavailable_never_returns_original_content()
    {
        var (a, _, id) = await Seed(true); var key = Guid.NewGuid();
        await using var db = fixture.Open();
        await new MessageService(db, new BlockService(db)).SendMessageAsync(a, key, id, new("private"));
        // Simulate historical access removed by a future retention/privacy operation.
        await db.ConversationMembershipPeriods.Where(p => p.ConversationId == id && p.UserId == a).ExecuteDeleteAsync();
        var receipt = (await new MessageReconciliation(db).ReconcileAsync(a, id, [new(key, "private")], default)).Single();
        Assert.Equal("Unavailable", receipt.State); Assert.Null(receipt.Message); Assert.Null(receipt.MessageId);
    }
}
