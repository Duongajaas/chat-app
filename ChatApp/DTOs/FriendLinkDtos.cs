namespace ChatApp.DTOs;

public record FriendLinkResponse(
    Guid Id,
    string Url,
    DateTime? ExpiresAt,
    int? MaxUses,
    int UsedCount,
    DateTime CreatedAt
);

public record PublicUserProfileResponse(
    Guid Id,
    string Username,
    string FullName,
    string? AvatarUrl,
    string RelationshipStatus
);