using ChatApp.Common;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChatApp.Tests;

public class GroupConversationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private async Task<(Guid[] Users, Guid Id)> Seed()
    {
        await using var db = fixture.Open();
        var users = Enumerable.Range(0, 5).Select(_ => new User { Username = Guid.NewGuid().ToString("N"), FullName = "Group test" }).ToArray();
        db.Users.AddRange(users);
        for (var i = 0; i < users.Length; i++)
            for (var j = i + 1; j < users.Length; j++)
            {
                var ids = new[] { users[i].Id, users[j].Id }.Order().ToArray();
                db.Friendships.Add(new Friendship { UserLowId = ids[0], UserHighId = ids[1] });
            }
        await db.SaveChangesAsync();
        var group = await new ConversationService(db).CreateConversationAsync(users[0].Id,
            new(ConversationType.Group, "Group test", [users[1].Id, users[2].Id]));
        return (users.Select(u => u.Id).ToArray(), group.Id);
    }
    private async Task Act(Func<GroupService, Task> action)
    {
        await using var db = fixture.Open(); await action(new GroupService(db));
    }
    private async Task<MessageResponse> Send(Guid user, Guid id, string content = "test")
    {
        await using var db = fixture.Open();
        return await new MessageService(db, new BlockService(db)).SendMessageAsync(user, Guid.NewGuid(), id, new(content));
    }
    private async Task<GroupInviteResponse> Invite(Guid actor, Guid id, int uses = 100)
    {
        await using var db = fixture.Open();
        return await new GroupService(db).CreateInviteAsync(actor, id, new(uses), Guid.NewGuid());
    }

    [Fact]
    public async Task Rejoin_reads_only_membership_intervals_in_history_states_preview_and_delete()
    {
        var (u, id) = await Seed();
        var old = await Send(u[0], id, "old");
        await Act(g => g.RemoveAsync(u[1], id, u[1], false));
        var gap = await Send(u[0], id, "gap");
        var invite = await Invite(u[0], id);
        await Act(g => g.JoinAsync(u[1], invite.Code!));
        await using (var db = fixture.Open())
        {
            var summary = await new ConversationService(db).GetConversationByIdAsync(u[1], id);
            Assert.Equal(old.Id, summary.LastMessageId);
            Assert.Equal(0, summary.UnreadCount);
            var messages = new MessageService(db, new BlockService(db));
            Assert.Equal(new[] { old.Id }, (await messages.GetMessagesAsync(u[1], id)).Messages.Select(m => m.Id));
            Assert.Empty(await messages.GetStatesAsync(u[1], id, [gap.Id]));
            Assert.Equal(403, (await Assert.ThrowsAsync<AppException>(() => messages.DeleteForMeAsync(u[1], gap.Id))).StatusCode);
        }
        var current = await Send(u[0], id, "current");
        await Act(g => g.RemoveAsync(u[1], id, u[1], false));
        var gap2 = await Send(u[0], id);
        await Act(g => g.JoinAsync(u[1], invite.Code!));
        await using var verify = fixture.Open();
        var visible = await MessageVisibility.Readable(verify, u[1], id).OrderBy(m => m.Sequence).Select(m => m.Id).ToArrayAsync();
        Assert.Equal(new[] { old.Id, current.Id }, visible);
        Assert.DoesNotContain(gap2.Id, visible);
        Assert.Equal(3, await verify.ConversationMembershipPeriods.CountAsync(p => p.UserId == u[1] && p.ConversationId == id));
    }

    [Fact]
    public async Task New_member_cannot_read_prejoin_messages_or_use_them_as_read_cursor()
    {
        var (u, id) = await Seed(); var prior = await Send(u[0], id);
        await Act(g => g.AddAsync(u[1], id, u[3]));
        await using var db = fixture.Open();
        Assert.Empty(await MessageVisibility.Readable(db, u[3], id).ToListAsync());
        Assert.Null((await new ConversationService(db).GetConversationByIdAsync(u[3], id)).LastMessageId);
        Assert.Empty(await new MessageService(db, new BlockService(db)).GetStatesAsync(u[3], id, [prior.Id]));
    }

    [Fact]
    public async Task Roles_kick_and_owner_transfer_are_enforced_and_audited()
    {
        var (u, id) = await Seed();
        await Assert.ThrowsAsync<AppException>(() => Act(g => g.RenameAsync(u[1], id, "denied")));
        await Act(g => g.SetRoleAsync(u[0], id, u[1], MemberRole.Admin));
        await Act(g => g.RenameAsync(u[1], id, "renamed"));
        await Assert.ThrowsAsync<AppException>(() => Act(g => g.SetRoleAsync(u[1], id, u[2], MemberRole.Admin)));
        await Assert.ThrowsAsync<AppException>(() => Act(g => g.RemoveAsync(u[1], id, u[0], true)));
        await Act(g => g.RemoveAsync(u[1], id, u[2], true));
        var invite = await Invite(u[0], id);
        await Assert.ThrowsAsync<AppException>(() => Act(g => g.JoinAsync(u[2], invite.Code!)));
        await Act(g => g.AddAsync(u[0], id, u[3]));
        await Assert.ThrowsAsync<AppException>(() => Act(g => g.AddAsync(u[3], id, u[2])));
        await Act(g => g.AddAsync(u[1], id, u[2]));
        await Act(g => g.RemoveAsync(u[0], id, u[0], false));
        await using var db = fixture.Open();
        Assert.Equal(u[1], (await db.ConversationMembers.SingleAsync(m => m.ConversationId == id && m.LeftAt == null && m.Role == MemberRole.Owner)).UserId);
        Assert.Single(await db.AuditLogs.Where(a => a.ConversationId == id && a.Action == "TRANSFER_OWNERSHIP").ToListAsync());
        Assert.Equal(MemberRole.Member, (await db.ConversationMembers.SingleAsync(m => m.UserId == u[2] && m.ConversationId == id)).Role);
    }

    [Fact]
    public async Task Concurrent_last_invite_use_and_retries_never_overconsume()
    {
        var (u, id) = await Seed(); var invite = await Invite(u[0], id, 1);
        async Task<bool> Join(Guid user)
        {
            try { await Act(g => g.JoinAsync(user, invite.Code!)); return true; }
            catch (AppException) { return false; }
        }
        var outcomes = await Task.WhenAll(Join(u[3]), Join(u[4]));
        Assert.Single(outcomes, x => x);
        var winner = outcomes[0] ? u[3] : u[4];
        await Act(g => g.JoinAsync(winner, invite.Code!));
        await using var db = fixture.Open();
        Assert.Equal(1, (await db.ConversationInvites.SingleAsync(i => i.Id == invite.Id)).UsedCount);
        Assert.Equal(4, await db.ConversationMembers.CountAsync(m => m.ConversationId == id && m.LeftAt == null));
        Assert.DoesNotContain(invite.Code!, (await db.ConversationInvites.SingleAsync(i => i.Id == invite.Id)).InviteCodeHash);
    }

    [Fact]
    public async Task Last_owner_closes_group_and_revokes_all_links_without_losing_history()
    {
        var (u, id) = await Seed(); var message = await Send(u[0], id); var invite = await Invite(u[0], id);
        await Task.WhenAll(Act(g => g.RemoveAsync(u[1], id, u[1], false)), Act(g => g.RemoveAsync(u[2], id, u[2], false)));
        await Act(g => g.RemoveAsync(u[0], id, u[0], false));
        await Act(g => g.RemoveAsync(u[0], id, u[0], false));
        await Assert.ThrowsAsync<AppException>(() => Act(g => g.JoinAsync(u[3], invite.Code!)));
        await Assert.ThrowsAsync<AppException>(() => Send(u[0], id));
        await using var db = fixture.Open();
        Assert.NotNull((await db.Conversations.FindAsync(id))!.ClosedAt);
        Assert.NotNull((await db.ConversationInvites.FindAsync(invite.Id))!.RevokedAt);
        Assert.Contains(await MessageVisibility.Readable(db, u[0], id).ToListAsync(), m => m.Id == message.Id);
        Assert.Contains((await new ConversationService(db).GetMyConversationsAsync(u[0])).Items, c => c.Id == id && c.HasLeft);
    }

    [Fact]
    public async Task Invite_receipt_deduplicates_without_revealing_token_twice()
    {
        var (u, id) = await Seed(); var key = Guid.NewGuid();
        await using var db = fixture.Open(); var groups = new GroupService(db);
        var first = await groups.CreateInviteAsync(u[0], id, new(), key);
        var retry = await groups.CreateInviteAsync(u[0], id, new(), key);
        Assert.Equal(first.Id, retry.Id); Assert.NotNull(first.Code); Assert.Null(retry.Code);
        await Assert.ThrowsAsync<AppException>(() => groups.CreateInviteAsync(u[0], id, new(2), key));
        Assert.Equal(1, await db.ConversationInvites.CountAsync(i => i.ConversationId == id));
    }

    [Fact]
    public async Task Block_preserves_membership_history_until_kick_and_blocks_direct_add()
    {
        var (u, id) = await Seed();
        var before = await Send(u[0], id);
        await using (var db = fixture.Open()) await new BlockService(db).BlockUserAsync(u[1], u[0]);
        var afterBlock = await Send(u[0], id);
        await Act(g => g.RemoveAsync(u[0], id, u[1], true));
        var afterKick = await Send(u[0], id);
        await using var verify = fixture.Open();
        var readable = await MessageVisibility.Readable(verify, u[1], id).Select(m => m.Id).ToListAsync();
        Assert.Contains(before.Id, readable); Assert.Contains(afterBlock.Id, readable); Assert.DoesNotContain(afterKick.Id, readable);
        await Assert.ThrowsAsync<AppException>(() => Act(g => g.AddAsync(u[0], id, u[1])));
    }

    [Fact]
    public async Task Last_capacity_slot_is_shared_by_all_api_contexts()
    {
        var (u, id) = await Seed();
        await using (var db = fixture.Open())
        {
            for (var n = 0; n < 96; n++)
            {
                var user = new User { Username = Guid.NewGuid().ToString("N"), FullName = "Capacity" };
                db.Users.Add(user);
                db.ConversationMembers.Add(new ConversationMember { ConversationId = id, UserId = user.Id });
                db.ConversationMembershipPeriods.Add(new ConversationMembershipPeriod { ConversationId = id, UserId = user.Id });
            }
            await db.SaveChangesAsync();
        }
        var invite = await Invite(u[0], id);
        async Task<bool> Join(Guid user)
        {
            try { await Act(g => g.JoinAsync(user, invite.Code!)); return true; }
            catch (AppException error) when (error.StatusCode == 409) { return false; }
        }
        Assert.Single(await Task.WhenAll(Join(u[3]), Join(u[4])), x => x);
        await using var verify = fixture.Open();
        Assert.Equal(100, await verify.ConversationMembers.CountAsync(m => m.ConversationId == id && m.LeftAt == null));
        Assert.Equal(1, (await verify.ConversationInvites.FindAsync(invite.Id))!.UsedCount);
    }

    [Fact]
    public async Task Revoked_or_expired_links_do_not_create_memberships()
    {
        var (u, id) = await Seed();
        var revoked = await Invite(u[0], id);
        await Act(g => g.RevokeAsync(u[0], id, revoked.Id));
        await Act(g => g.RevokeAsync(u[0], id, revoked.Id));
        await Assert.ThrowsAsync<AppException>(() => Act(g => g.JoinAsync(u[3], revoked.Code!)));
        var expired = await Invite(u[0], id);
        await using (var db = fixture.Open())
            await db.ConversationInvites.Where(i => i.Id == expired.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        await Assert.ThrowsAsync<AppException>(() => Act(g => g.JoinAsync(u[3], expired.Code!)));
        await using var verify = fixture.Open();
        Assert.False(await verify.ConversationMembers.AnyAsync(m => m.ConversationId == id && m.UserId == u[3]));
    }

    private sealed class FailJoinSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData data,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<ConversationMembershipPeriod>().Any(e => e.State == EntityState.Added))
                throw new IOException("Injected failure after invite UPDATE");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Failure_after_consuming_invite_rolls_back_usage_membership_audit_and_outbox()
    {
        var (u, id) = await Seed(); var invite = await Invite(u[0], id);
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(new FailJoinSave()).Options))
            await Assert.ThrowsAsync<IOException>(() => new GroupService(db).JoinAsync(u[3], invite.Code!));
        await using var verify = fixture.Open();
        Assert.Equal(0, (await verify.ConversationInvites.FindAsync(invite.Id))!.UsedCount);
        Assert.False(await verify.ConversationMembers.AnyAsync(m => m.ConversationId == id && m.UserId == u[3]));
        Assert.False(await verify.AuditLogs.AnyAsync(a => a.ConversationId == id && a.Action == "JOIN_VIA_INVITE"));
        Assert.Equal(2, await verify.OutboxEvents.CountAsync(e => e.ConversationId == id)); // create group + invite
    }

    [Fact]
    public async Task Concurrent_owner_and_admin_leave_keep_exactly_one_remaining_owner()
    {
        var (u, id) = await Seed();
        await Act(g => g.SetRoleAsync(u[0], id, u[1], MemberRole.Admin));
        await Task.WhenAll(Act(g => g.RemoveAsync(u[0], id, u[0], false)), Act(g => g.RemoveAsync(u[1], id, u[1], false)));
        await using var db = fixture.Open();
        var remaining = await db.ConversationMembers.SingleAsync(m => m.ConversationId == id && m.LeftAt == null);
        Assert.Equal(u[2], remaining.UserId); Assert.Equal(MemberRole.Owner, remaining.Role);
        Assert.Null((await db.Conversations.FindAsync(id))!.ClosedAt);
    }

    [Fact]
    public async Task Invite_expiry_accepts_offsets_and_stores_utc()
    {
        var (u, id) = await Seed();
        var expires = DateTimeOffset.UtcNow.AddDays(2).ToOffset(TimeSpan.FromHours(7));
        await using var db = fixture.Open();
        var invite = await new GroupService(db).CreateInviteAsync(u[0], id, new(5, expires));
        Assert.Equal(expires.UtcDateTime, invite.ExpiresAt);
    }

    [Theory]
    [InlineData(MemberRole.Owner, MemberRole.Owner, false)]
    [InlineData(MemberRole.Owner, MemberRole.Admin, true)]
    [InlineData(MemberRole.Owner, MemberRole.Member, true)]
    [InlineData(MemberRole.Admin, MemberRole.Owner, false)]
    [InlineData(MemberRole.Admin, MemberRole.Admin, false)]
    [InlineData(MemberRole.Admin, MemberRole.Member, true)]
    [InlineData(MemberRole.Member, MemberRole.Owner, false)]
    [InlineData(MemberRole.Member, MemberRole.Admin, false)]
    [InlineData(MemberRole.Member, MemberRole.Member, false)]
    public void Kick_matrix(MemberRole actor, MemberRole target, bool allowed) =>
        Assert.Equal(allowed, GroupPermissionMatrix.CanKick(actor, target));
}
