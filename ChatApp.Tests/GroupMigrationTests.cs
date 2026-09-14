using ChatApp.Models;
using ChatApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChatApp.Tests;

public class GroupMigrationTests
{
    [Fact]
    public async Task Upgrade_backfills_periods_hashes_legacy_invites_and_closes_empty_groups()
    {
        var fixture = new PostgresFixture { InitialMigration = "20260908155557_ProductionReliability" };
        await fixture.InitializeAsync();
        try
        {
            await using var db = fixture.Open();
            var owner = new User { Username = Guid.NewGuid().ToString("N"), FullName = "Legacy" };
            db.Users.Add(owner); await db.SaveChangesAsync();
            var active = Guid.NewGuid(); var empty = Guid.NewGuid(); var invite = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO conversations (id, type, name, created_at, updated_at)
                  VALUES ({active}, 1, 'Legacy active', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                         ({empty}, 1, 'Legacy empty', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
                INSERT INTO conversation_members (id, conversation_id, user_id, role, joined_at, unread_count, is_pinned, last_read_sequence)
                  VALUES ({Guid.NewGuid()}, {active}, {owner.Id}, 0, CURRENT_TIMESTAMP, 0, false, 0);
                INSERT INTO conversation_invites (id, conversation_id, invite_code, max_uses, used_count, created_at)
                  VALUES ({invite}, {active}, 'legacy-link', 10, 0, CURRENT_TIMESTAMP),
                         ({Guid.NewGuid()}, {empty}, 'empty-link', 10, 0, CURRENT_TIMESTAMP);
                """);
            await db.Database.MigrateAsync();
            var period = await db.ConversationMembershipPeriods.SingleAsync(p => p.ConversationId == active);
            Assert.Equal(owner.Id, period.UserId); Assert.Equal(0, period.StartSequence); Assert.Null(period.EndSequence);
            Assert.Equal(GroupService.Hash("legacy-link"), (await db.ConversationInvites.FindAsync(invite))!.InviteCodeHash);
            Assert.NotNull((await db.Conversations.FindAsync(empty))!.ClosedAt);
            Assert.NotNull((await db.ConversationInvites.SingleAsync(i => i.ConversationId == empty)).RevokedAt);
            Assert.Null((await db.Conversations.FindAsync(active))!.ClosedAt);
        }
        finally { await fixture.DisposeAsync(); }
    }
}
