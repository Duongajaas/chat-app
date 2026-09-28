using ChatApp.Common;
using ChatApp.DTOs;
using ChatApp.Media;
using ChatApp.Models;
using ChatApp.Services;
using Microsoft.EntityFrameworkCore;
using ChatApp.Data;
using ChatApp.Hubs;
using ChatApp.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Concurrent;
using System.Text.Json;
using Xunit;

namespace ChatApp.Tests;

public class MessageFeaturesTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private async Task<(User[] Users, Guid Group)> Seed()
    {
        await using var db = fixture.Open();
        var users = Enumerable.Range(0, 4).Select(_ => new User { Username = Guid.NewGuid().ToString("N"), FullName = "Features" }).ToArray();
        db.Users.AddRange(users);
        for (int i = 1; i < users.Length; i++)
        {
            var pair = new[] { users[0].Id, users[i].Id }.Order().ToArray();
            db.Friendships.Add(new Friendship { UserLowId = pair[0], UserHighId = pair[1] });
        }
        await db.SaveChangesAsync();
        var group = await new ConversationService(db).CreateConversationAsync(users[0].Id,
            new(ConversationType.Group, "Features", [users[1].Id]));
        return (users, group.Id);
    }
    private async Task<MessageResponse> Send(Guid user, Guid group, string content = "text", Guid? reply = null, MentionInput[]? mentions = null, Guid? key = null)
    {
        await using var db = fixture.Open();
        return await new MessageService(db, new BlockService(db)).SendMessageAsync(user, key ?? Guid.NewGuid(), group, new(content, reply, mentions));
    }
    private async Task Add(Guid owner, Guid group, Guid user)
    {
        await using var db = fixture.Open(); await new GroupService(db).AddAsync(owner, group, user);
    }
    private async Task Pin(Guid user, Guid group, Guid message)
    {
        await using var db = fixture.Open(); await new PinService(db).SetAsync(user, group, message, true);
    }

    [Fact]
    public async Task Reply_preview_is_per_reader_and_recall_invalidates_it()
    {
        var (u, id) = await Seed();
        var original = await Send(u[0].Id, id, "secret-before-join");
        await Add(u[0].Id, id, u[2].Id);
        var reply = await Send(u[1].Id, id, "public-after-join", original.Id);
        await using (var db = fixture.Open())
        {
            var reader = new MessageReader(db);
            Assert.True((await reader.ReadAsync(u[1].Id, id, [reply.Id])).Single().ReplyPreview!.IsAvailable);
            var restricted = (await reader.ReadAsync(u[2].Id, id, [reply.Id])).Single().ReplyPreview!;
            Assert.False(restricted.IsAvailable); Assert.Null(restricted.ContentSnippet); Assert.Null(restricted.SenderName);
            var events = await db.OutboxEvents.Where(e => e.ConversationId == id && e.EventName == "MessageAvailable").ToListAsync();
            Assert.Single(events); Assert.DoesNotContain("secret-before-join", events[0].Payload);
            Assert.DoesNotContain("public-after-join", events[0].Payload);
            await new MessageService(db, new BlockService(db)).DeleteMessageAsync(u[0].Id, original.Id);
        }
        await using var verify = fixture.Open();
        Assert.False((await new MessageReader(verify).ReadAsync(u[1].Id, id, [reply.Id])).Single().ReplyPreview!.IsAvailable);
        await Assert.ThrowsAsync<AppException>(() => Send(u[2].Id, id, "invalid", original.Id));
    }

    [Fact]
    public async Task Pins_filter_periods_and_recall_frees_a_slot()
    {
        var (u, id) = await Seed(); var old = new List<MessageResponse>();
        for (int i = 0; i < 3; i++) { var m = await Send(u[0].Id, id); old.Add(m); await Pin(u[1].Id, id, m.Id); }
        await Add(u[0].Id, id, u[2].Id);
        for (int i = 0; i < 2; i++) { var m = await Send(u[0].Id, id); await Pin(u[2].Id, id, m.Id); }
        var extra = await Send(u[0].Id, id);
        Assert.Equal(409, (await Assert.ThrowsAsync<AppException>(() => Pin(u[2].Id, id, extra.Id))).StatusCode);
        await using (var db = fixture.Open())
        {
            Assert.Equal(2, (await new PinService(db).ListAsync(u[2].Id, id)).Count);
            Assert.Equal(5, (await new PinService(db).ListAsync(u[1].Id, id)).Count);
            await new MessageService(db, new BlockService(db)).DeleteMessageAsync(u[0].Id, old[0].Id);
        }
        await Pin(u[2].Id, id, extra.Id);
        await Pin(u[2].Id, id, extra.Id);
        await using var verify = fixture.Open();
        Assert.Equal(5, await verify.PinnedMessages.CountAsync(p => p.ConversationId == id));
        Assert.False(await verify.PinnedMessages.AnyAsync(p => p.MessageId == old[0].Id));
    }

    [Fact]
    public async Task Concurrent_pin_requests_cannot_exceed_global_limit()
    {
        var (u, id) = await Seed(); var messages = new List<MessageResponse>();
        for (int i = 0; i < 8; i++) messages.Add(await Send(u[0].Id, id));
        var results = await Task.WhenAll(messages.Select(async m => {
            try { await Pin(u[1].Id, id, m.Id); return true; }
            catch (AppException e) when (e.StatusCode == 409) { return false; }
        }));
        Assert.Equal(5, results.Count(x => x));
        await using var db = fixture.Open(); Assert.Equal(5, await db.PinnedMessages.CountAsync(p => p.ConversationId == id));
    }

    [Fact]
    public async Task Mentions_preserve_utf16_positions_remove_self_duplicates_and_inactive_targets()
    {
        var (u, id) = await Seed();
        var text = $"😀 @{u[1].Username} @{u[1].Username} @{u[0].Username} @{u[2].Username}";
        var parts = text.Split(' '); var offset = 3;
        var mentions = new List<MentionInput>();
        foreach (var (target, token) in new[] { (u[1], parts[1]), (u[1], parts[2]), (u[0], parts[3]), (u[2], parts[4]) })
        { mentions.Add(new(target.Id, offset, token.Length)); offset += token.Length + 1; }
        var sent = await Send(u[0].Id, id, text, mentions: mentions.ToArray());
        var mention = Assert.Single(sent.Mentions!); Assert.Equal(u[1].Id, mention.UserId); Assert.Equal(3, mention.Start);
        Assert.Equal(u[1].Username, mention.Username);
        await using var db = fixture.Open();
        Assert.Single(await db.Notifications.Where(n => n.UserId == u[1].Id && n.Type == "mention").ToListAsync());
        Assert.Single(await db.OutboxEvents.Where(e => e.ConversationId == id && e.EventName == "MentionReceived").ToListAsync());
        await Assert.ThrowsAsync<AppException>(() => Send(u[0].Id, id, "@wrong", mentions: [new(u[1].Id, 0, 6)]));
        await Assert.ThrowsAsync<AppException>(() => Send(u[0].Id, id, "short", mentions: [new(u[1].Id, int.MaxValue, 10)]));
    }

    [Fact]
    public async Task Forward_copies_attachments_and_retries_without_duplicate_unread_or_events()
    {
        var (u, id) = await Seed(); var source = await Send(u[0].Id, id, "copy");
        Guid target;
        await using (var db = fixture.Open())
        {
            db.MessageAttachments.Add(new MessageAttachment { MessageId = source.Id, StorageKey = "private/key", FileName = "secret.txt" });
            await db.SaveChangesAsync();
            target = (await new ConversationService(db).CreateConversationAsync(u[0].Id,
                new(ConversationType.Group, "Target", [u[1].Id]))).Id;
        }
        var key = Guid.NewGuid(); MessageResponse forwarded;
        await using (var db = fixture.Open())
            forwarded = await new MessageService(db, new BlockService(db)).ForwardAsync(u[1].Id, source.Id, target, key);
        await using (var db = fixture.Open())
        {
            await new MessageService(db, new BlockService(db)).DeleteMessageAsync(u[0].Id, source.Id);
            var retry = await new MessageService(db, new BlockService(db)).ForwardAsync(u[1].Id, source.Id, target, key);
            Assert.Equal(forwarded.Id, retry.Id); Assert.True(retry.IsForwarded); Assert.Equal("copy", retry.Content);
            Assert.Single(retry.Attachments!); Assert.Null(retry.ReplyPreview); Assert.Empty(retry.Mentions!);
        }
        await using var verify = fixture.Open();
        Assert.Equal(1, await verify.Messages.CountAsync(m => m.ConversationId == target));
        Assert.Equal(1, (await verify.ConversationMembers.SingleAsync(m => m.ConversationId == target && m.UserId == u[0].Id)).UnreadCount);
        Assert.Equal("private/key", (await verify.MessageAttachments.SingleAsync(a => a.MessageId == forwarded.Id)).StorageKey);
        Assert.Single(await verify.OutboxEvents.Where(e => e.ConversationId == target && e.EventName == "MessageAvailable").ToListAsync());
    }

    [Fact]
    public async Task Forward_rejects_source_outside_membership_period_without_side_effects()
    {
        var (u, id) = await Seed(); var old = await Send(u[0].Id, id);
        await Add(u[0].Id, id, u[2].Id);
        await using var db = fixture.Open();
        await Assert.ThrowsAsync<AppException>(() => new MessageService(db, new BlockService(db)).ForwardAsync(u[2].Id, old.Id, id, Guid.NewGuid()));
        Assert.Equal(1, await db.Messages.CountAsync(m => m.ConversationId == id));
    }

    [Fact]
    public async Task Member_avatar_updates_asset_version_audit_and_outbox_atomically()
    {
        var (u, id) = await Seed(); var storage = new FakeStorage();
        await using var db = fixture.Open(); var service = new AvatarService(db, storage);
        var intent = await service.PrepareAsync(u[1].Id, id);
        var version = (await db.Conversations.SingleAsync(c => c.Id == id)).Version;
        await Assert.ThrowsAsync<AppException>(() => service.ApplyAsync(u[0].Id, id, intent.AssetId, version));
        await Assert.ThrowsAsync<AppException>(() => service.ApplyAsync(u[1].Id, id, intent.AssetId, version + 1));
        db.ChangeTracker.Clear();
        Assert.Null((await db.Conversations.SingleAsync(c => c.Id == id)).AvatarAssetId);
        await service.ApplyAsync(u[1].Id, id, intent.AssetId, version);
        await service.ApplyAsync(u[1].Id, id, intent.AssetId, version);
        db.ChangeTracker.Clear();
        var group = await db.Conversations.SingleAsync(c => c.Id == id);
        Assert.Equal(intent.AssetId, group.AvatarAssetId); Assert.Equal(version + 1, group.Version);
        Assert.NotNull((await db.AvatarAssets.SingleAsync(a => a.Id == intent.AssetId)).VerifiedAt);
        Assert.Single(await db.AuditLogs.Where(a => a.ConversationId == id && a.Action == "CHANGE_AVATAR").ToListAsync());
        Assert.True(await db.OutboxEvents.AnyAsync(e => e.ConversationId == id && e.EventName == "ConversationChanged"));
    }

    [Fact]
    public async Task Avatar_rechecks_membership_after_remote_verification()
    {
        var (u, id) = await Seed(); var storage = new FakeStorage();
        await using var db = fixture.Open(); var service = new AvatarService(db, storage);
        var intent = await service.PrepareAsync(u[1].Id, id);
        var version = (await db.Conversations.SingleAsync(c => c.Id == id)).Version;
        storage.BeforeInspect = async () => { await using var other = fixture.Open(); await new GroupService(other).RemoveAsync(u[0].Id, id, u[1].Id, true); };
        await Assert.ThrowsAsync<AppException>(() => service.ApplyAsync(u[1].Id, id, intent.AssetId, version));
        db.ChangeTracker.Clear();
        Assert.Null((await db.Conversations.SingleAsync(c => c.Id == id)).AvatarAssetId);
        Assert.Null((await db.AvatarAssets.SingleAsync(a => a.Id == intent.AssetId)).AttachedAt);
    }

    [Theory]
    [InlineData("muted", true)]
    [InlineData("left", false)]
    [InlineData("hidden", false)]
    [InlineData("recalled", false)]
    public async Task Mention_worker_rechecks_recipient_before_delivery(string state, bool shouldReceive)
    {
        var (u, id) = await Seed();
        var content = "@" + u[1].Username;
        var sent = await Send(u[0].Id, id, content, mentions: [new(u[1].Id, 0, content.Length)]);
        Guid eventId;
        await using (var db = fixture.Open())
        {
            eventId = (await db.OutboxEvents.SingleAsync(e => e.ConversationId == id && e.EventName == "MentionReceived")).Id;
            var member = await db.ConversationMembers.SingleAsync(m => m.ConversationId == id && m.UserId == u[1].Id);
            member.MutedUntil = DateTime.UtcNow.AddDays(1); await db.SaveChangesAsync();
            if (state == "left") await new GroupService(db).RemoveAsync(u[1].Id, id, u[1].Id, false);
            if (state == "hidden") await new MessageService(db, new BlockService(db)).DeleteForMeAsync(u[1].Id, sent.Id);
            if (state == "recalled") await new MessageService(db, new BlockService(db)).DeleteMessageAsync(u[0].Id, sent.Id);
        }
        var delivered = new ConcurrentBag<string>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Users(It.IsAny<IReadOnlyList<string>>())).Returns((IReadOnlyList<string> recipients) => {
            var proxy = new Mock<IClientProxy>();
            proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Callback((string name, object?[] args, CancellationToken _) => {
                    if (name == "MentionReceived" && args[0] is JsonElement payload && payload.GetProperty("messageId").GetGuid() == sent.Id)
                        foreach (var recipient in recipients) delivered.Add(recipient);
                }).Returns(Task.CompletedTask);
            return proxy.Object;
        });
        var hub = new Mock<IHubContext<ChatHub>>(); hub.SetupGet(h => h.Clients).Returns(clients.Object);
        using var services = new ServiceCollection().AddScoped<AppDbContext>(_ => fixture.Open()).BuildServiceProvider();
        using var worker = new OutboxWorker(services.GetRequiredService<IServiceScopeFactory>(), hub.Object, NullLogger<OutboxWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (true)
            {
                await using var db = fixture.Open();
                if (await db.OutboxEvents.AnyAsync(e => e.Id == eventId && e.ProcessedAt != null, timeout.Token)) break;
                await Task.Delay(50, timeout.Token);
            }
            Assert.Equal(shouldReceive, delivered.Contains(u[1].Id.ToString()));
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Failed_avatar_verification_does_not_change_group()
    {
        var (u, id) = await Seed(); var storage = new FakeStorage { Invalid = true };
        await using var db = fixture.Open(); var service = new AvatarService(db, storage);
        var intent = await service.PrepareAsync(u[1].Id, id);
        var version = (await db.Conversations.SingleAsync(c => c.Id == id)).Version;
        await Assert.ThrowsAsync<AppException>(() => service.ApplyAsync(u[1].Id, id, intent.AssetId, version));
        db.ChangeTracker.Clear();
        Assert.Null((await db.Conversations.SingleAsync(c => c.Id == id)).AvatarAssetId);
        Assert.False(await db.AuditLogs.AnyAsync(a => a.ConversationId == id && a.Action == "CHANGE_AVATAR"));
    }

    [Fact]
    public async Task Retrying_message_with_different_reply_is_a_conflict()
    {
        var (u, id) = await Seed(); var original = await Send(u[0].Id, id); var key = Guid.NewGuid();
        await Send(u[1].Id, id, "reply", original.Id, key: key);
        Assert.Equal(409, (await Assert.ThrowsAsync<AppException>(() => Send(u[1].Id, id, "reply", key: key))).StatusCode);
    }

    private sealed class FakeStorage : IAvatarStorage
    {
        public bool IsConfigured => true;
        public Func<Task>? BeforeInspect { get; set; }
        public bool Invalid { get; set; }
        public AvatarUpload Prepare(string publicId) => new("https://example.test/upload", new() { ["public_id"] = publicId });
        public async Task<VerifiedAvatar> InspectAsync(string publicId, CancellationToken ct)
        {
            if (BeforeInspect != null) await BeforeInspect();
            return new("provider-asset", publicId, 1, 1024, 128, 128, Invalid ? "svg" : "png", "https://example.test/avatar.png");
        }
        public Task DeleteAsync(string publicId, CancellationToken ct) => Task.CompletedTask;
    }
}
