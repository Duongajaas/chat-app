using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Hubs;
using ChatApp.Models;
using ChatApp.Realtime;
using ChatApp.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ChatApp.Tests;
public class ChatReliabilityTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static MessageService Messages(AppDbContext db) => new(db, new BlockService(db));
    private async Task<(Guid a, Guid b, Guid c, Guid conversation)> Seed(bool direct = false)
    {
        await using var db = fixture.Open();
        var users = Enumerable.Range(0, 3).Select(_ => new User { Username = Guid.NewGuid().ToString("N"), FullName = "Test" }).ToArray();
        db.Users.AddRange(users); await db.SaveChangesAsync();
        foreach (var other in users.Skip(1))
        {
            var pair = new[] { users[0].Id, other.Id }.Order().ToArray();
            db.Friendships.Add(new Friendship { UserLowId = pair[0], UserHighId = pair[1] });
        }
        await db.SaveChangesAsync();
        var service = new ConversationService(db);
        var conversation = direct ? await service.GetOrCreateDirectConversationAsync(users[0].Id, users[1].Id)
            : await service.CreateConversationAsync(users[0].Id, new(ConversationType.Group, "Test group", [users[1].Id, users[2].Id]));
        return (users[0].Id, users[1].Id, users[2].Id, conversation.Id);
    }
    private async Task<MessageResponse> Send(Guid user, Guid conversation, Guid? id = null, string content = "hello")
    {
        await using var db = fixture.Open();
        return await Messages(db).SendMessageAsync(user, id ?? Guid.NewGuid(), conversation, new(content));
    }

    [Fact]
    public async Task Concurrent_duplicate_has_one_message_unread_and_outbox()
    {
        var (a, b, _, id) = await Seed(); var client = Guid.NewGuid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Send(a, id, client)));
        Assert.Single(responses.Select(r => r.Id).Distinct());
        await using var db = fixture.Open();
        Assert.Equal(1, await db.Messages.CountAsync(m => m.ConversationId == id));
        Assert.Equal(1, (await db.ConversationMembers.SingleAsync(m => m.UserId == b && m.ConversationId == id)).UnreadCount);
        Assert.Equal(1, await db.OutboxEvents.CountAsync(e => e.ConversationId == id && e.EventName == "ReceiveMessage"));
        Assert.Equal(responses[0].Id, (await db.Conversations.FindAsync(id))!.LastMessageId);
    }

    [Fact]
    public async Task Reused_id_with_other_content_returns_conflict()
    {
        var (a, _, _, id) = await Seed(); var key = Guid.NewGuid();
        await Send(a, id, key);
        var error = await Assert.ThrowsAsync<AppException>(() => Send(a, id, key, "different"));
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task Block_and_nonmember_cannot_send_and_hub_has_no_send_method()
    {
        var (a, b, outsider, id) = await Seed(true);
        await using var db = fixture.Open();
        Assert.Equal(403, (await Assert.ThrowsAsync<AppException>(() => Send(outsider, id))).StatusCode);
        await new BlockService(db).BlockUserAsync(b, a);
        Assert.Equal(403, (await Assert.ThrowsAsync<AppException>(() => Send(a, id))).StatusCode);
        Assert.Null(typeof(ChatHub).GetMethod("SendMessage"));
        Assert.Empty(await ConversationEvents.RecipientsAsync(db, id, default));
    }

    [Fact]
    public async Task Newer_and_older_pages_are_bounded_and_complete()
    {
        var (a, b, _, id) = await Seed();
        for (var n = 0; n < 13; n++) await Send(a, id, content: n.ToString());
        await using var db = fixture.Open();
        long cursor = 0; var seen = new HashSet<Guid>();
        MessageListResponse page;
        do {
            page = await Messages(db).GetMessagesAsync(b, id, after: cursor, limit: 4);
            Assert.InRange(page.Messages.Count, 0, 4);
            foreach (var m in page.Messages) Assert.True(seen.Add(m.Id));
            cursor = page.NextCursor ?? cursor;
        } while (page.HasMore);
        Assert.Equal(13, seen.Count);
        var newest = await Messages(db).GetMessagesAsync(b, id, limit: 4);
        Assert.True(newest.HasMore);
        var older = await Messages(db).GetMessagesAsync(b, id, before: newest.NextCursor, limit: 4);
        Assert.True(older.Messages[^1].Sequence < newest.Messages[0].Sequence);
    }

    [Fact]
    public async Task Read_cursor_never_regresses_or_erases_concurrent_new_message()
    {
        var (a, b, _, id) = await Seed();
        var first = await Send(a, id);
        var second = await Send(a, id);
        await using var db = fixture.Open();
        await Task.WhenAll(Messages(db).MarkAsReadAsync(b, id, second.Sequence), Send(a, id));
        await Messages(db).MarkAsReadAsync(b, id, first.Sequence);
        db.ChangeTracker.Clear();
        var member = await db.ConversationMembers.SingleAsync(m => m.UserId == b && m.ConversationId == id);
        Assert.Equal(second.Sequence, member.LastReadSequence);
        Assert.Equal(1, member.UnreadCount);
    }

    [Fact]
    public async Task Hide_keeps_membership_leave_limits_history_and_transfers_owner()
    {
        var (a, b, _, id) = await Seed(); var first = await Send(b, id);
        await using var db = fixture.Open(); var service = new ConversationService(db);
        await service.DeleteConversationAsync(a, id);
        Assert.True(await service.IsMemberAsync(a, id));
        await service.LeaveConversationAsync(a, id);
        await Send(b, id, content: "after leave");
        var history = await Messages(db).GetMessagesAsync(a, id);
        Assert.Single(history.Messages); Assert.Equal(first.Id, history.Messages[0].Id);
        Assert.DoesNotContain(a.ToString(), await ConversationEvents.RecipientsAsync(db, id, default));
        Assert.Equal(1, await db.ConversationMembers.CountAsync(m => m.ConversationId == id && m.LeftAt == null && m.Role == MemberRole.Owner));
        Assert.Equal(403, (await Assert.ThrowsAsync<AppException>(() => Send(a, id))).StatusCode);
    }

    [Fact]
    public async Task Delete_for_me_and_recall_persist_and_reconcile_old_messages()
    {
        var (a, b, _, id) = await Seed(); var first = await Send(a, id); var second = await Send(a, id);
        await using var db = fixture.Open(); var service = Messages(db);
        await service.DeleteForMeAsync(b, first.Id);
        Assert.DoesNotContain((await service.GetMessagesAsync(b, id)).Messages, m => m.Id == first.Id);
        Assert.Contains((await service.GetMessagesAsync(a, id)).Messages, m => m.Id == first.Id);
        await service.DeleteMessageAsync(a, second.Id);
        var states = await service.GetStatesAsync(b, id, [first.Id, second.Id]);
        Assert.True(states.Single(s => s.Id == first.Id).Hidden);
        Assert.True(states.Single(s => s.Id == second.Id).Deleted);
        Assert.Equal("Deleted", (await service.GetMessagesAsync(b, id)).Messages.Single().Status);
    }

    [Fact]
    public async Task Sender_outside_time_window_cannot_recall_but_owner_can()
    {
        var (owner, sender, _, id) = await Seed(); var message = await Send(sender, id);
        await using var db = fixture.Open();
        await db.Messages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.CreatedAt, DateTime.UtcNow.AddHours(-1)));
        Assert.Equal(403, (await Assert.ThrowsAsync<AppException>(() => Messages(db).DeleteMessageAsync(sender, message.Id))).StatusCode);
        await Messages(db).DeleteMessageAsync(owner, message.Id);
    }

    [Fact]
    public async Task Invalid_text_does_not_create_messages()
    {
        var (a, _, _, id) = await Seed();
        foreach (var content in new[] { "", "   ", new string('x', 4001) })
            Assert.Equal(400, (await Assert.ThrowsAsync<AppException>(() => Send(a, id, content: content))).StatusCode);
    }

    [Fact]
    public async Task Conversation_cursor_is_stable_when_activity_changes()
    {
        var (a, b, c, _) = await Seed();
        await using var db = fixture.Open(); var service = new ConversationService(db);
        for (int i = 0; i < 5; i++) await service.CreateConversationAsync(a, new(ConversationType.Group, "Group", [b, c]));
        var first = await service.GetMyConversationsAsync(a, limit: 2);
        await Send(a, first.Items[0].Id);
        var all = first.Items.Select(c => c.Id).ToHashSet();
        var cursor = first.NextCursor;
        while (cursor != null) {
            var page = await service.GetMyConversationsAsync(a, cursor, 2);
            foreach (var item in page.Items) Assert.True(all.Add(item.Id));
            cursor = page.NextCursor;
        }
        Assert.Equal(6, all.Count);
    }

    private sealed class FailOutboxSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<OutboxEvent>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("Injected failure before commit");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Failure_before_outbox_commit_rolls_back_message_unread_and_metadata()
    {
        var (a, b, _, id) = await Seed();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(new FailOutboxSave()).Options))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Messages(db).SendMessageAsync(a, Guid.NewGuid(), id, new("rollback")));
        await using var verify = fixture.Open();
        Assert.False(await verify.Messages.AnyAsync(m => m.ConversationId == id));
        Assert.Equal(0, (await verify.ConversationMembers.SingleAsync(m => m.ConversationId == id && m.UserId == b)).UnreadCount);
        Assert.Null((await verify.Conversations.FindAsync(id))!.LastMessageId);
    }

    [Fact]
    public async Task Worker_retries_committed_event_and_excludes_member_who_left()
    {
        var (a, b, _, id) = await Seed(); var message = await Send(a, id);
        await using (var db = fixture.Open()) await new ConversationService(db).LeaveConversationAsync(b, id);
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(fixture.ConnectionString).UseSnakeCaseNamingConvention());
        await using var provider = services.BuildServiceProvider();
        var proxy = new Mock<IClientProxy>();
        var failedOnce = false;
        proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns((string name, object?[] args, CancellationToken _) =>
            {
                if (name == "ReceiveMessage" && args[0] is System.Text.Json.JsonElement payload &&
                    payload.GetProperty("id").GetGuid() == message.Id && !failedOnce)
                {
                    failedOnce = true;
                    return Task.FromException(new IOException("Injected transport failure"));
                }
                return Task.CompletedTask;
            });
        var clients = new Mock<IHubClients>();
        var observed = new System.Collections.Concurrent.ConcurrentBag<string[]>();
        clients.Setup(c => c.Users(It.IsAny<IReadOnlyList<string>>())).Returns((IReadOnlyList<string> users) => {
            observed.Add(users.ToArray()); return proxy.Object;
        });
        var hub = new Mock<IHubContext<ChatHub>>(); hub.SetupGet(h => h.Clients).Returns(clients.Object);
        using var worker = new OutboxWorker(provider.GetRequiredService<IServiceScopeFactory>(), hub.Object, NullLogger<OutboxWorker>.Instance);
        await worker.StartAsync(default);
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true) {
                await using var db = fixture.Open();
                if (await db.OutboxEvents.AnyAsync(e => e.ConversationId == id && e.EventName == "ReceiveMessage" && e.ProcessedAt != null)) break;
                await Task.Delay(100, timeout.Token);
            }
            Assert.True(failedOnce);
            Assert.Contains(observed, ids => ids.Contains(a.ToString()) && !ids.Contains(b.ToString()));
            await using var verified = fixture.Open();
            Assert.Equal(1, (await verified.OutboxEvents.SingleAsync(e => e.ConversationId == id && e.EventName == "ReceiveMessage")).Attempts);
        } finally { await worker.StopAsync(default); }
    }
}
