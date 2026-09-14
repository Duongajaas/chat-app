using ChatApp.Data;
using ChatApp.Messages;
using ChatApp.Realtime;
using MediatR;

namespace ChatApp.Notifications;

// Compatibility for other publishers: durable delivery follows the same recipient checks.
public class MessageSentSignalRHandler(AppDbContext db) : INotificationHandler<MessageSentNotification>
{
    public async Task Handle(MessageSentNotification notification, CancellationToken cancellationToken)
    {
        ConversationEvents.Add(db, notification.ConversationId, "ReceiveMessage", notification.Message);
        await db.SaveChangesAsync(cancellationToken);
    }
}
