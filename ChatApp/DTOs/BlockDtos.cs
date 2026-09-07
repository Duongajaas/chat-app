namespace ChatApp.DTOs;

public record BlockedUserResponse(
    Guid Id,
    string Username,
    string FullName,
    string? AvatarUrl,
    DateTime BlockedAt
);
