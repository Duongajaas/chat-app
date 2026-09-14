using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Options;
using ChatApp.Services;
using ChatApp.Realtime;
using ChatApp.Presence;
using ChatApp.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ChatApp.Tests;

public class RealtimeIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private const string Key = "integration-test-only-signing-key-0123456789";
    private WebApplicationFactory<Program> Factory(string? redisPrefix = null, bool runOutbox = true, string? role = null) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing");
        // These settings select services during Program startup, before ConfigureAppConfiguration runs.
        builder.UseSetting("Redis:Enabled", (redisPrefix != null).ToString());
        builder.UseSetting("Runtime:Role", role ?? (runOutbox ? "all" : "api"));
        if (redisPrefix != null)
        {
            builder.UseSetting("Redis:ChannelPrefix", redisPrefix);
            builder.UseSetting("ConnectionStrings:Redis", Environment.GetEnvironmentVariable("CHATAPP_TEST_REDIS")
                ?? throw new InvalidOperationException("Set CHATAPP_TEST_REDIS to an isolated Redis test server."));
        }
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString,
            ["Jwt:SecretKey"] = Key, ["Jwt:Issuer"] = "tests", ["Jwt:Audience"] = "tests",
            ["Cors:AllowedOrigins:0"] = "https://localhost",
            ["Logging:LogLevel:Default"] = "Warning"
        }));
    });
    private static string Token(User user) => new TokenService(Microsoft.Extensions.Options.Options.Create(new JwtOptions { SecretKey = Key, Issuer = "tests", Audience = "tests" })).GenerateAccessToken(user).Token;
    private static HttpClient Client(WebApplicationFactory<Program> factory, User? user = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (user != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(user));
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return client;
    }
    private static HubConnection Hub(WebApplicationFactory<Program> factory, User user) => new HubConnectionBuilder()
        .WithUrl("https://localhost/hubs/chat", options => {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            options.AccessTokenProvider = () => Task.FromResult<string?>(Token(user));
        }).Build();
    private async Task<User[]> Users(bool friends = false)
    {
        await using var db = fixture.Open();
        var users = Enumerable.Range(0, 3).Select(_ => new User { Username = Guid.NewGuid().ToString("N"), FullName = "Integration" }).ToArray();
        db.Users.AddRange(users); await db.SaveChangesAsync();
        foreach (var other in friends ? users.Skip(1) : Enumerable.Empty<User>())
        {
            var pair = new[] { users[0].Id, other.Id }.Order().ToArray();
            db.Friendships.Add(new Friendship { UserLowId = pair[0], UserHighId = pair[1] });
        }
        await db.SaveChangesAsync(); return users;
    }
    [Fact]
    public async Task Group_rejoin_on_second_api_does_not_receive_gap_events_from_delayed_worker()
    {
        var users = await Users(true);
        var prefix = "group-period-test-" + Guid.NewGuid().ToString("N");
        await using var producer = Factory(prefix, runOutbox: false);
        await using var consumer = Factory(prefix, runOutbox: false);
        using var sender = Client(producer, users[0]);
        using var returning = Client(consumer, users[1]);
        using var newcomer = Client(consumer, users[2]);
        var created = await sender.PostAsJsonAsync("/api/conversations/group",
            new { type = "Group", name = "Periods", memberIds = new[] { users[1].Id } });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<ConversationSummaryResponse>(Json))!.Id;
        async Task<MessageResponse> Send(string content)
        {
            var response = await sender.PostAsJsonAsync($"/api/conversations/{id}/messages",
                new { clientMessageId = Guid.NewGuid(), content });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<MessageResponse>(Json))!;
        }
        var before = await Send("before");
        (await returning.PostAsync($"/api/conversations/{id}/leave", null)).EnsureSuccessStatusCode();
        var gap = await Send("gap");
        var invitation = await sender.PostAsJsonAsync($"/api/conversations/{id}/invites", new { maxUses = 10 });
        invitation.EnsureSuccessStatusCode();
        var code = (await invitation.Content.ReadFromJsonAsync<GroupInviteResponse>(Json))!.Code;
        (await returning.PostAsJsonAsync("/api/conversations/join", new { code })).EnsureSuccessStatusCode();
        (await newcomer.PostAsJsonAsync("/api/conversations/join", new { code })).EnsureSuccessStatusCode();
        var current = await Send("current");

        var returningEvents = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        var newEvents = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        await using var firstHub = Hub(consumer, users[1]);
        await using var secondHub = Hub(consumer, users[2]);
        firstHub.On<MessageResponse>("ReceiveMessage", m => returningEvents.Add(m.Id));
        secondHub.On<MessageResponse>("ReceiveMessage", m => newEvents.Add(m.Id));
        await firstHub.StartAsync(); await secondHub.StartAsync();
        await using var worker = Factory(prefix, role: "worker");
        using var workerClient = Client(worker);
        (await workerClient.GetAsync("/health/ready")).EnsureSuccessStatusCode();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            await using var db = fixture.Open();
            if (!await db.OutboxEvents.AnyAsync(e => e.ConversationId == id && e.ProcessedAt == null)) break;
            await Task.Delay(100, timeout.Token);
        }
        while (!returningEvents.Contains(current.Id) || !newEvents.Contains(current.Id))
            await Task.Delay(100, timeout.Token);
        Assert.Contains(before.Id, returningEvents);
        Assert.DoesNotContain(gap.Id, returningEvents);
        Assert.DoesNotContain(before.Id, newEvents);
        Assert.DoesNotContain(gap.Id, newEvents);
        var history = await returning.GetFromJsonAsync<MessageListResponse>($"/api/conversations/{id}/messages", Json);
        Assert.Equal(new[] { before.Id, current.Id }, history!.Messages.Select(m => m.Id));
    }

    [Fact]
    public async Task Redis_delivers_outbox_events_to_another_instance_and_isolates_other_deployments()
    {
        var users = await Users();
        var prefix = "chatapp-test-" + Guid.NewGuid().ToString("N");
        await using var producer = Factory(prefix, runOutbox: false);
        // Neither API delivers the outbox. A separate worker must publish across Redis.
        await using var consumer = Factory(prefix, runOutbox: false);
        await using var unrelated = Factory(prefix + "-other", runOutbox: false);
        await using var worker = Factory(prefix, role: "worker");
        using var sender = Client(producer, users[0]);
        using var receiver = Client(consumer, users[1]);
        using var otherClient = Client(unrelated, users[1]);
        Assert.IsType<Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisHubLifetimeManager<ChatApp.Hubs.ChatHub>>(
            producer.Services.GetRequiredService<Microsoft.AspNetCore.SignalR.HubLifetimeManager<ChatApp.Hubs.ChatHub>>());
        Assert.IsType<Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisHubLifetimeManager<ChatApp.Hubs.ChatHub>>(
            consumer.Services.GetRequiredService<Microsoft.AspNetCore.SignalR.HubLifetimeManager<ChatApp.Hubs.ChatHub>>());
        (await receiver.GetAsync("/health/ready")).EnsureSuccessStatusCode();
        var response = await sender.PostAsJsonAsync("/api/conversations/direct", new { otherUserId = users[1].Id });
        response.EnsureSuccessStatusCode();
        var conversation = (await response.Content.ReadFromJsonAsync<ConversationSummaryResponse>(Json))!;
        var received = Channel.CreateUnbounded<MessageResponse>();
        var leaked = Channel.CreateUnbounded<MessageResponse>();
        await using var remoteHub = Hub(consumer, users[1]);
        await using var unrelatedHub = Hub(unrelated, users[1]);
        remoteHub.On<MessageResponse>("ReceiveMessage", m => received.Writer.TryWrite(m));
        unrelatedHub.On<MessageResponse>("ReceiveMessage", m => leaked.Writer.TryWrite(m));
        await remoteHub.StartAsync();
        await unrelatedHub.StartAsync();
        await remoteHub.InvokeAsync("JoinConversation", conversation.Id);
        await unrelatedHub.InvokeAsync("JoinConversation", conversation.Id);

        var clientMessageId = Guid.NewGuid();
        var ack = await sender.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages",
            new { clientMessageId, content = "Across Redis" });
        ack.EnsureSuccessStatusCode();
        var message = (await ack.Content.ReadFromJsonAsync<MessageResponse>())!;
        Assert.DoesNotContain(producer.Services.GetServices<IHostedService>(), s => s is OutboxWorker);
        Assert.DoesNotContain(consumer.Services.GetServices<IHostedService>(), s => s is OutboxWorker);
        await using (var db = fixture.Open())
            Assert.True(await db.OutboxEvents.AnyAsync(e => e.ConversationId == conversation.Id && e.ProcessedAt == null));
        using var workerClient = Client(worker);
        Assert.Equal(HttpStatusCode.NotFound, (await workerClient.GetAsync("/api/conversations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await workerClient.GetAsync("/swagger/index.html")).StatusCode);
        (await workerClient.GetAsync("/health/ready")).EnsureSuccessStatusCode();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.Equal(message.Id, (await received.Reader.ReadAsync(timeout.Token)).Id);
        var persisted = await receiver.GetFromJsonAsync<MessageListResponse>($"/api/conversations/{conversation.Id}/messages");
        Assert.Contains(persisted!.Messages, m => m.Id == message.Id);

        // Reconnect and recover an event missed while the browser was offline from PostgreSQL.
        await remoteHub.StopAsync();
        var offline = await sender.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages",
            new { clientMessageId = Guid.NewGuid(), content = "Recover from database" });
        offline.EnsureSuccessStatusCode();
        await remoteHub.StartAsync();
        var backlog = await receiver.GetFromJsonAsync<MessageListResponse>($"/api/conversations/{conversation.Id}/messages?after={message.Sequence}");
        Assert.Contains(backlog!.Messages, m => m.Content == "Recover from database");
        await Task.Delay(300);
        Assert.False(leaked.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Redis_http_and_hub_limits_are_shared_between_api_instances()
    {
        var prefix = "chatapp-test-" + Guid.NewGuid().ToString("N");
        await using var first = Factory(prefix, runOutbox: false);
        await using var second = Factory(prefix, runOutbox: false);
        using var a = Client(first);
        using var b = Client(second);
        for (var i = 0; i < 5; i++)
        {
            var response = await (i % 2 == 0 ? a : b).PostAsJsonAsync("/api/auth/login", new { });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        var rejected = await b.PostAsJsonAsync("/api/auth/login", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);

        var users = await Users();
        await using var hubA = Hub(first, users[0]);
        await using var hubB = Hub(second, users[0]);
        await hubA.StartAsync();
        await hubB.StartAsync();
        // LeaveConversation is a cheap hub method; both connections belong to the same user.
        for (var i = 0; i < 60; i++) await (i % 2 == 0 ? hubA : hubB).InvokeAsync("LeaveConversation", Guid.NewGuid());
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => hubB.InvokeAsync("LeaveConversation", Guid.NewGuid()));
        await using var anotherUser = Hub(second, users[1]);
        await anotherUser.StartAsync();
        await anotherUser.InvokeAsync("LeaveConversation", Guid.NewGuid());
    }

    [Fact]
    public async Task Expired_presence_is_published_offline_only_to_authorized_users()
    {
        var users = await Users();
        await using (var db = fixture.Open()) await new ConversationService(db).GetOrCreateDirectConversationAsync(users[0].Id, users[1].Id);
        var prefix = "chatapp-test-" + Guid.NewGuid().ToString("N");
        await using var api = Factory(prefix, runOutbox: false).WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.PostConfigure<PresenceOptions>(o =>
            { o.HeartbeatSeconds = 1; o.ConnectionTtlSeconds = 3; o.SweepSeconds = 1; })));
        await using var worker = Factory(prefix, role: "worker").WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.PostConfigure<PresenceOptions>(o =>
            { o.HeartbeatSeconds = 1; o.ConnectionTtlSeconds = 3; o.SweepSeconds = 1; })));
        using var client = Client(api);
        using var workerClient = Client(worker);
        var permitted = Channel.CreateUnbounded<JsonElement>();
        var outsider = Channel.CreateUnbounded<JsonElement>();
        await using var friendHub = Hub(api, users[1]);
        await using var otherHub = Hub(api, users[2]);
        friendHub.On<JsonElement>("UserPresenceChanged", p => permitted.Writer.TryWrite(p));
        otherHub.On<JsonElement>("UserPresenceChanged", p => outsider.Writer.TryWrite(p));
        await friendHub.StartAsync();
        await otherHub.StartAsync();
        // An orphan connection simulates a killed API: nobody renews or explicitly removes it.
        await api.Services.GetRequiredService<RedisPresenceStore>().RenewAsync(users[0].Id, "orphan");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        bool sawOnline = false;
        while (true)
        {
            var change = await permitted.Reader.ReadAsync(timeout.Token);
            if (change.GetProperty("userId").GetGuid() != users[0].Id) continue;
            if (change.GetProperty("isOnline").GetBoolean()) sawOnline = true;
            else { Assert.True(sawOnline); break; }
        }
        Assert.False(await api.Services.GetRequiredService<IPresenceTracker>().IsOnlineAsync(users[0].Id));
        Assert.False(outsider.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Rest_ack_realtime_reconnect_and_membership_revocation_work_together()
    {
        var users = await Users(true);
        await using var factory = Factory();
        using var sender = Client(factory, users[0]);
        using var receiver = Client(factory, users[1]);
        var created = await sender.PostAsJsonAsync("/api/conversations/group", new { type = "Group", name = "Integration", memberIds = new[] { users[1].Id, users[2].Id } });
        created.EnsureSuccessStatusCode();
        var conversation = (await created.Content.ReadFromJsonAsync<ConversationSummaryResponse>(Json))!;
        var events = Channel.CreateUnbounded<MessageResponse>();
        await using var connection = Hub(factory, users[1]);
        connection.On<MessageResponse>("ReceiveMessage", message => events.Writer.TryWrite(message));
        await connection.StartAsync();
        await connection.InvokeAsync("JoinConversation", conversation.Id);
        var ack = await sender.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new { clientMessageId = Guid.NewGuid(), content = "first" });
        ack.EnsureSuccessStatusCode();
        var sent = (await ack.Content.ReadFromJsonAsync<MessageResponse>())!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.Equal(sent.Id, (await events.Reader.ReadAsync(timeout.Token)).Id);
        await connection.StopAsync();
        var offlineAck = await sender.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new { clientMessageId = Guid.NewGuid(), content = "offline" });
        offlineAck.EnsureSuccessStatusCode();
        await connection.StartAsync();
        await connection.InvokeAsync("JoinConversation", conversation.Id);
        var backlog = await receiver.GetFromJsonAsync<MessageListResponse>($"/api/conversations/{conversation.Id}/messages?after={sent.Sequence}");
        Assert.Contains(backlog!.Messages, m => m.Content == "offline");
        await connection.InvokeAsync("MarkAsRead", conversation.Id, backlog.Messages[^1].Sequence);
        var summary = await receiver.GetFromJsonAsync<ConversationSummaryResponse>($"/api/conversations/{conversation.Id}", Json);
        Assert.Equal(0, summary!.UnreadCount);
        (await receiver.PostAsync($"/api/conversations/{conversation.Id}/leave", null)).EnsureSuccessStatusCode();
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => connection.InvokeAsync("JoinConversation", conversation.Id));
        (await sender.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new { clientMessageId = Guid.NewGuid(), content = "after leave" })).EnsureSuccessStatusCode();
        // Allow the worker to deliver; the receiver must not get the post-leave payload.
        await Task.Delay(1500);
        var received = new List<MessageResponse>(); while (events.Reader.TryRead(out var item)) received.Add(item);
        Assert.DoesNotContain(received, m => m.Content == "after leave");
        var history = await receiver.GetFromJsonAsync<MessageListResponse>($"/api/conversations/{conversation.Id}/messages");
        Assert.DoesNotContain(history!.Messages, m => m.Content == "after leave");
    }

    [Fact]
    public async Task Unauthorized_invalid_payload_and_unknown_hub_send_are_rejected()
    {
        var users = await Users(); await using var factory = Factory();
        using var anonymous = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/conversations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/ready")).StatusCode);
        using var client = Client(factory, users[0]);
        var direct = await client.PostAsJsonAsync("/api/conversations/direct", new { otherUserId = users[1].Id });
        direct.EnsureSuccessStatusCode();
        var id = (await direct.Content.ReadFromJsonAsync<ConversationSummaryResponse>(Json))!.Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/conversations/{id}/messages", new { content = "missing id" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/conversations/{id}/messages", new { clientMessageId = Guid.NewGuid(), content = "   " })).StatusCode);
        await using var hub = Hub(factory, users[0]); await hub.StartAsync();
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => hub.InvokeAsync("SendMessage", id, "bypass"));
    }

    [Fact]
    public async Task Presence_is_not_broadcast_to_unrelated_or_blocked_users()
    {
        var users = await Users();
        await using (var db = fixture.Open()) await new ConversationService(db).GetOrCreateDirectConversationAsync(users[0].Id, users[1].Id);
        await using var factory = Factory();
        var allowed = Channel.CreateUnbounded<JsonElement>();
        var outsider = Channel.CreateUnbounded<JsonElement>();
        await using var b = Hub(factory, users[1]); await using var c = Hub(factory, users[2]);
        b.On<JsonElement>("UserPresenceChanged", p => allowed.Writer.TryWrite(p));
        c.On<JsonElement>("UserPresenceChanged", p => outsider.Writer.TryWrite(p));
        await b.StartAsync(); await c.StartAsync();
        await using var a = Hub(factory, users[0]); await a.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(users[0].Id, (await allowed.Reader.ReadAsync(timeout.Token)).GetProperty("userId").GetGuid());
        await Task.Delay(200); Assert.False(outsider.Reader.TryRead(out _));
        await using (var db = fixture.Open()) await new BlockService(db).BlockUserAsync(users[1].Id, users[0].Id);
        await a.StopAsync(); await a.StartAsync();
        await Task.Delay(200); Assert.False(allowed.Reader.TryRead(out _));
    }
}
