using ChatApp.DTOs;
using MediatR;

namespace ChatApp.Messages;

public record MessageSentNotification(
    Guid ConversationId,
    Guid SenderId,
    MessageResponse Message
) : INotification;
