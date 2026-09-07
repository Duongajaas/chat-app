using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();

    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<FriendLink> FriendLinks => Set<FriendLink>();

    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<DirectConversation> DirectConversations => Set<DirectConversation>();
    public DbSet<ConversationMember> ConversationMembers => Set<ConversationMember>();
    public DbSet<ConversationInvite> ConversationInvites => Set<ConversationInvite>();

    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();
    public DbSet<MessageMention> MessageMentions => Set<MessageMention>();
    public DbSet<MessageReaction> MessageReactions => Set<MessageReaction>();
    public DbSet<MessageRead> MessageReads => Set<MessageRead>();
    public DbSet<MessageDeletion> MessageDeletions => Set<MessageDeletion>();
    public DbSet<MessageDraft> MessageDrafts => Set<MessageDraft>();
    public DbSet<ScheduledMessage> ScheduledMessages => Set<ScheduledMessage>();

    public DbSet<CallSession> CallSessions => Set<CallSession>();
    public DbSet<CallParticipant> CallParticipants => Set<CallParticipant>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PinnedMessage> PinnedMessages => Set<PinnedMessage>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // ---------- Users & Auth ----------
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.GoogleId).IsUnique();
            e.Property(u => u.FullName).HasMaxLength(100);
            e.Property(u => u.Username).HasMaxLength(50);
            e.Property(u => u.Email).HasMaxLength(255);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.FamilyId });
            e.HasIndex(x => x.ExpiresAt);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Device).WithMany()
                .HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("password_reset_tokens");
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.PasswordResetTokens)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserDevice>(e =>
        {
            e.ToTable("user_devices");
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.DeviceToken }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Friend & Block ----------
        modelBuilder.Entity<FriendRequest>(e =>
        {
            e.ToTable("friend_requests");
            e.HasIndex(x => x.SenderId);
            e.HasIndex(x => x.ReceiverId);
            e.HasIndex(x => new { x.SenderId, x.ReceiverId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ReceiverId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_friend_requests_no_self", "sender_id <> receiver_id"));
        });

        modelBuilder.Entity<Friendship>(e =>
        {
            e.ToTable("friendships");
            e.HasIndex(x => new { x.UserLowId, x.UserHighId }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserLowId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserHighId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_friendships_low_lt_high", "user_low_id < user_high_id"));
        });

        modelBuilder.Entity<Block>(e =>
        {
            e.ToTable("blocks");
            e.HasIndex(x => new { x.BlockerId, x.BlockedId }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BlockerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BlockedId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_blocks_no_self", "blocker_id <> blocked_id"));
        });

        modelBuilder.Entity<FriendLink>(e =>
        {
            e.ToTable("friend_links");
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.Token).HasMaxLength(64);
            e.Property(x => x.TokenHash).HasMaxLength(128);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Conversation ----------
        modelBuilder.Entity<Conversation>(e =>
        {
            e.ToTable("conversations");
            e.HasIndex(x => x.Type);
            e.HasIndex(x => x.LastMessageAt);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.SetNull);
            // FK vòng tới messages: KHÔNG cascade để tránh multiple cascade paths, xử lý null ở tầng service.
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.LastMessageId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DirectConversation>(e =>
        {
            e.ToTable("direct_conversations");
            e.HasIndex(x => x.ConversationId).IsUnique();
            e.HasIndex(x => new { x.UserLowId, x.UserHighId }).IsUnique();
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserLowId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserHighId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConversationMember>(e =>
        {
            e.ToTable("conversation_members");
            e.HasIndex(x => new { x.ConversationId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.ConversationId);
            e.Property(x => x.RequestStatus).HasDefaultValue(MemberRequestStatus.Accepted);
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.LastReadMessageId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ConversationInvite>(e =>
        {
            e.ToTable("conversation_invites");
            e.HasIndex(x => x.ConversationId);
            e.HasIndex(x => x.InviteCode).IsUnique();
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.SetNull);
        });

        // ---------- Message ----------
        modelBuilder.Entity<Message>(e =>
        {
            e.ToTable("messages");
            e.Property(x => x.Sequence).UseIdentityAlwaysColumn();
            e.HasIndex(x => new { x.SenderId, x.ClientMessageId }).IsUnique();
            e.HasIndex(x => new { x.ConversationId, x.Sequence });
            e.HasIndex(x => x.SenderId);
            e.HasIndex(x => x.DeletedAt);
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.ReplyToMessageId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.ForwardedFromMessageId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MessageAttachment>(e =>
        {
            e.ToTable("message_attachments");
            e.HasIndex(x => x.MessageId);
            e.Property(x => x.Metadata).HasColumnType("jsonb");
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MessageMention>(e =>
        {
            e.ToTable("message_mentions");
            e.HasIndex(x => new { x.MessageId, x.MentionedUserId }).IsUnique();
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.MentionedUserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MessageReaction>(e =>
        {
            e.ToTable("message_reactions");
            e.HasIndex(x => new { x.MessageId, x.UserId }).IsUnique();
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MessageRead>(e =>
        {
            e.ToTable("message_reads");
            e.HasIndex(x => new { x.MessageId, x.UserId }).IsUnique();
            e.HasIndex(x => new { x.ConversationId, x.UserId });
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MessageDeletion>(e =>
        {
            e.ToTable("message_deletions");
            e.HasIndex(x => new { x.MessageId, x.UserId }).IsUnique();
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MessageDraft>(e =>
        {
            e.ToTable("message_drafts");
            e.HasIndex(x => new { x.ConversationId, x.UserId }).IsUnique();
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduledMessage>(e =>
        {
            e.ToTable("scheduled_messages");
            e.HasIndex(x => x.ScheduledAt);
            e.HasIndex(x => x.SenderId);
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Call ----------
        modelBuilder.Entity<CallSession>(e =>
        {
            e.ToTable("call_sessions");
            e.HasIndex(x => x.ConversationId);
            e.HasIndex(x => x.CallerId);
            e.HasIndex(x => x.StartedAt);
            e.HasIndex(x => x.Status);
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CallerId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CallParticipant>(e =>
        {
            e.ToTable("call_participants");
            e.HasIndex(x => new { x.CallSessionId, x.UserId }).IsUnique();
            e.HasOne<CallSession>().WithMany().HasForeignKey(x => x.CallSessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        // ---------- Notification / Pin / Report / Audit ----------
        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.UserId, x.IsRead });
            e.Property(x => x.Data).HasColumnType("jsonb");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PinnedMessage>(e =>
        {
            e.ToTable("pinned_messages");
            e.HasIndex(x => new { x.ConversationId, x.MessageId }).IsUnique();
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.PinnedBy).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Report>(e =>
        {
            e.ToTable("reports");
            e.HasIndex(x => x.ReporterId);
            e.HasIndex(x => new { x.TargetType, x.TargetId });
            e.HasIndex(x => x.Status);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasIndex(x => x.ConversationId);
            e.HasIndex(x => x.ActorId);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.Property(x => x.Metadata).HasColumnType("jsonb");
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
