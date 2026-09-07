using System.ComponentModel.DataAnnotations;

namespace ChatApp.DTOs;

public record SendFriendRequestRequest(
    [Required] Guid ReceiverId
);

public record FriendRequestResponse(
    Guid Id,
    Guid SenderId,
    Guid ReceiverId,
    string Status,
    DateTime CreatedAt
);

public record PendingFriendRequestResponse(
    Guid Id,
    Guid SenderId,
    string Username,
    string FullName,
    string? AvatarUrl,
    DateTime CreatedAt
);

public record FriendResponse(
    Guid Id,
    string Username,
    string FullName,
    string? AvatarUrl,
    DateTime FriendSince
);