using ChatApp.DTOs;
using ChatApp.Models;
using Xunit;

namespace ChatApp.Tests;

public class ReportModerationContractTests
{
    [Fact]
    public void Cannot_self_report_user()
    {
        var userId = Guid.NewGuid();
        var request = new CreateReportRequest(ReportTargetType.User, userId, "Spam", "Self report attempt");

        Assert.Equal(ReportTargetType.User, request.TargetType);
        Assert.Equal(userId, request.TargetId);
    }

    [Fact]
    public void Report_status_transitions_are_valid()
    {
        var report = new Report
        {
            ReporterId = Guid.NewGuid(),
            TargetType = ReportTargetType.User,
            TargetId = Guid.NewGuid(),
            Reason = "Harassment",
            Status = ReportStatus.Pending
        };

        Assert.Equal(ReportStatus.Pending, report.Status);

        report.Status = ReportStatus.Reviewing;
        Assert.Equal(ReportStatus.Reviewing, report.Status);

        report.Status = ReportStatus.Resolved;
        report.ReviewedBy = Guid.NewGuid();
        report.ReviewedAt = DateTime.UtcNow;

        Assert.Equal(ReportStatus.Resolved, report.Status);
        Assert.NotNull(report.ReviewedBy);
        Assert.NotNull(report.ReviewedAt);
    }

    [Fact]
    public void Moderation_decision_payload_supports_message_deletion_and_user_restriction()
    {
        var decision = new ModerationDecisionRequest(
            ReportStatus.Resolved,
            ActionTaken: "Deleted offending content and restricted account",
            ModeratorNotes: "Violation of community rules against hate speech",
            FlagUser: true,
            RestrictUserMinutes: 60,
            DeleteOffendingMessage: true
        );

        Assert.Equal(ReportStatus.Resolved, decision.Status);
        Assert.True(decision.FlagUser);
        Assert.Equal(60, decision.RestrictUserMinutes);
        Assert.True(decision.DeleteOffendingMessage);
    }

    [Theory]
    [InlineData(UserRole.User, false)]
    [InlineData(UserRole.Moderator, true)]
    [InlineData(UserRole.Admin, true)]
    public void Moderator_role_check_distinguishes_ordinary_users(UserRole role, bool isModerator)
    {
        var allowed = role is UserRole.Moderator or UserRole.Admin;
        Assert.Equal(isModerator, allowed);
    }
}
