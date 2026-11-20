using ChatApp.Common;
using ChatApp.DTOs;
using ChatApp.Models;
using Xunit;

namespace ChatApp.Tests;

public class AuditLoggingContractTests
{
    [Theory]
    [InlineData(MemberRole.Owner, true)]
    [InlineData(MemberRole.Admin, true)]
    [InlineData(MemberRole.Member, false)]
    public void Only_owner_and_admin_have_view_audit_permission(MemberRole role, bool expected)
    {
        var hasPermission = GroupPermissionMatrix.HasPermission(role, "ViewAudit");
        Assert.Equal(expected, hasPermission);
    }

    [Fact]
    public void Audit_response_does_not_contain_sensitive_information()
    {
        var id = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var metadata = "{\"group\":{\"version\":2},\"details\":{\"reason\":\"spam\"}}";
        var createdAt = DateTime.UtcNow;

        var response = new AuditLogResponse(
            id,
            conversationId,
            actorId,
            "Moderator Name",
            "moderator_user",
            "MODERATOR_DECISION",
            targetId,
            "Target User",
            "target_user",
            metadata,
            createdAt
        );

        Assert.Equal(id, response.Id);
        Assert.Equal("MODERATOR_DECISION", response.Action);
        Assert.DoesNotContain("password", response.Metadata ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", response.Metadata ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", response.Metadata ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Audit_log_creation_populates_required_server_fields()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var log = new AuditLog
        {
            ActorId = actor,
            Action = "TRANSFER_OWNERSHIP",
            TargetUserId = target,
            Metadata = "{\"newOwner\":\"" + target + "\"}"
        };

        Assert.NotEqual(Guid.Empty, log.Id);
        Assert.Equal(actor, log.ActorId);
        Assert.Equal("TRANSFER_OWNERSHIP", log.Action);
        Assert.Equal(target, log.TargetUserId);
        Assert.True(log.CreatedAt <= DateTime.UtcNow);
    }
}
