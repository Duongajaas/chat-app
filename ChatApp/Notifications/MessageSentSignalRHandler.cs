using ChatApp.Hubs;
using ChatApp.Messages;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Notifications;

public class MessageSentSignalRHandler : INotificationHandler<MessageSentNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public MessageSentSignalRHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task Handle(MessageSentNotification notification, CancellationToken cancellationToken)
    {
        await _hubContext.Clients
            .Group(notification.ConversationId.ToString())
            .SendAsync("ReceiveMessage", notification.Message, cancellationToken);
    }
}
