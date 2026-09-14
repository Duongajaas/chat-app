using ChatApp.Models;

namespace ChatApp.Common;

public static class GroupPermissionMatrix
{
    public static string[] Permissions(MemberRole role) => role switch
    {
        MemberRole.Owner => ["RenameGroup", "AddMember", "ManageRoles", "ManageInvites", "DeleteOthersMessage", "ViewAudit"],
        MemberRole.Admin => ["RenameGroup", "AddMember", "ManageInvites", "DeleteOthersMessage", "ViewAudit"],
        MemberRole.Member => ["AddMember"],
        _ => []
    };
    public static bool HasPermission(MemberRole role, string permission) => Permissions(role).Contains(permission);
    public static bool CanKick(MemberRole actor, MemberRole target) =>
        target != MemberRole.Owner && (actor == MemberRole.Owner || actor == MemberRole.Admin && target == MemberRole.Member);
}
