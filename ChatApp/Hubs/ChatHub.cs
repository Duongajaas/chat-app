using ChatApp.Common;
using ChatApp.Data;
using ChatApp.Models;
using ChatApp.Presence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Hubs;

public class ChatHub : Hub
{
    private readonly AppDbContext _db;
    private readonly IPresenceTracker _presenceTracker;
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(8);

    public ChatHub(AppDbContext db, IPresenceTracker presenceTracker)
    {
        _db = db;
        _presenceTracker = presenceTracker;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            Context.Abort();
            return;
        }

        await _presenceTracker.AddConnectionAsync(userId.Value, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, "user-" + userId.Value);

        await Clients.All.SendAsync("UserPresenceChanged", new
        {
            UserId = userId.Value,
            IsOnline = true,
            LastSeenAt = DateTime.UtcNow
        });

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetCurrentUserId();
        if (userId is not null)
        {
            await _presenceTracker.RemoveConnectionAsync(userId.Value, Context.ConnectionId);

            await Task.Delay(GracePeriod);

            if (await _presenceTracker.IsOnlineAsync(userId.Value))
            {
                await base.OnDisconnectedAsync(exception);
                return;
            }

            await Clients.All.SendAsync("UserPresenceChanged", new
            {
                UserId = userId.Value,
                IsOnline = false,
                LastSeenAt = DateTime.UtcNow
            });
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinConversation(Guid conversationId)
    {
        await EnsureConversationMembershipAsync(conversationId);
        await Groups.AddToGroupAsync(Context.ConnectionId, conversationId.ToString());
    }

    public async Task LeaveConversation(Guid conversationId)
    {
        await EnsureConversationMembershipAsync(conversationId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, conversationId.ToString());
    }

    public async Task SendMessage(Guid conversationId, string content)
    {
        await EnsureConversationMembershipAsync(conversationId);
        await Clients.Group(conversationId.ToString()).SendAsync("ReceiveMessage", new
        {
            ConversationId = conversationId,
            SenderId = GetCurrentUserId(),
            Content = content,
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task Typing(Guid conversationId, bool isTyping)
    {
        await EnsureConversationMembershipAsync(conversationId);
        await Clients.Group(conversationId.ToString()).SendAsync("TypingIndicator", new
        {
            ConversationId = conversationId,
            UserId = GetCurrentUserId(),
            IsTyping = isTyping,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task MarkAsRead(Guid conversationId)
    {
        await EnsureConversationMembershipAsync(conversationId);
        await Clients.Group(conversationId.ToString()).SendAsync("MessagesRead", new
        {
            ConversationId = conversationId,
            UserId = GetCurrentUserId(),
            ReadAt = DateTime.UtcNow
        });
    }

    private async Task EnsureConversationMembershipAsync(Guid conversationId)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            throw new HubException("Unauthorized");
        }

        var isMember = await _db.ConversationMembers
            .AnyAsync(x => x.ConversationId == conversationId && x.UserId == userId && x.LeftAt == null);

        if (!isMember)
        {
            throw new HubException("Bạn không có quyền truy cập cuộc trò chuyện này.");
        }
    }

    private Guid? GetCurrentUserId()
    {
        var raw = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
