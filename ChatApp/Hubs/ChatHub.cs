using System.Security.Claims;
using ChatApp.Data;
using ChatApp.Presence;
using ChatApp.Realtime;
using ChatApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Hubs;

[Authorize]
public class ChatHub(AppDbContext db, IPresenceTracker presence, IMessageService messages, PresenceEvents presenceEvents) : Hub
{
    private Guid UserId => Guid.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public override async Task OnConnectedAsync()
    {
        await presence.AddConnectionAsync(UserId, Context.ConnectionId);
        await presenceEvents.NotifyLocalAsync(UserId);
        await RefreshPresence();
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await presence.RemoveConnectionAsync(UserId, Context.ConnectionId);
        await presenceEvents.NotifyLocalAsync(UserId);
        await base.OnDisconnectedAsync(exception);
    }

    // User-targeted delivery is independent of room subscriptions, so stale joins cannot leak messages.
    public async Task JoinConversation(Guid conversationId) => await EnsureMemberAsync(conversationId);
    public Task LeaveConversation(Guid conversationId) => Task.CompletedTask;

    public Task RefreshPresence() => presenceEvents.SendSnapshotAsync(UserId, Clients.Caller, Context.ConnectionAborted);

    public async Task Typing(Guid conversationId, bool isTyping)
    {
        await using var tx = await db.Database.BeginTransactionAsync(Context.ConnectionAborted);
        await ConversationLock.AcquireAsync(db, conversationId, Context.ConnectionAborted);
        await EnsureMemberAsync(conversationId);
        var recipients = await ConversationEvents.RecipientsAsync(db, conversationId, Context.ConnectionAborted);
        await Clients.Users(recipients).SendAsync("TypingIndicator", new
        { ConversationId = conversationId, UserId, IsTyping = isTyping }, Context.ConnectionAborted);
        await tx.CommitAsync(Context.ConnectionAborted);
    }

    public Task MarkAsRead(Guid conversationId, long sequence) =>
        messages.MarkAsReadAsync(UserId, conversationId, sequence, Context.ConnectionAborted);

    private async Task EnsureMemberAsync(Guid id)
    {
        if (!await db.ConversationMembers.AnyAsync(m => m.ConversationId == id && m.UserId == UserId && m.LeftAt == null && db.Conversations.Any(c => c.Id == id && c.ClosedAt == null && c.DeletedAt == null)))
            throw new HubException("Bạn không có quyền truy cập hội thoại.");
    }

}
