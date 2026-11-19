using System.ComponentModel.DataAnnotations;
using ChatApp.Models;

namespace ChatApp.DTOs;

public record RenameGroupRequest([Required, StringLength(100, MinimumLength = 1)] string Name);
public record AddGroupMemberRequest(Guid UserId);
public record ChangeGroupRoleRequest([EnumDataType(typeof(MemberRole))] MemberRole Role);
public record CreateGroupInviteRequest(int MaxUses = 100, DateTimeOffset? ExpiresAt = null);
public record JoinGroupRequest([Required, StringLength(100, MinimumLength = 10)] string Code);
public record GroupMemberResponse(Guid UserId, string FullName, string? AvatarUrl, MemberRole Role,
    DateTime JoinedAt, bool CanKick, bool CanChangeRole, string Username = "");
public record GroupInviteResponse(Guid Id, Guid? CreatedBy, int? MaxUses, int UsedCount,
    DateTime? ExpiresAt, DateTime? RevokedAt, string? Code = null);

public record TransferOwnershipRequest([Required] Guid NewOwnerId);
public record AuditLogResponse(
    Guid Id,
    Guid? ConversationId,
    Guid? ActorId,
    string? ActorName,
    string? ActorUsername,
    string Action,
    Guid? TargetUserId,
    string? TargetName,
    string? TargetUsername,
    string? Metadata,
    DateTime CreatedAt
);
