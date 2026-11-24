using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatApp.Migrations
{
    /// <inheritdoc />
    public partial class SyncCurrentModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_conversation_members_conversation_id",
                table: "conversation_members");

            migrationBuilder.RenameColumn(
                name: "invite_code",
                table: "conversation_invites",
                newName: "invite_code_hash");

            migrationBuilder.RenameIndex(
                name: "ix_conversation_invites_invite_code",
                table: "conversation_invites",
                newName: "ix_conversation_invites_invite_code_hash");

            migrationBuilder.AddColumn<string>(
                name: "close_reason",
                table: "conversations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "closed_at",
                table: "conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "conversations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "last_read_sequence",
                table: "conversation_members",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "leave_reason",
                table: "conversation_members",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "left_at_sequence",
                table: "conversation_members",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "removed_by_user_id",
                table: "conversation_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_conversation_members_conversation_id_user_id",
                table: "conversation_members",
                columns: new[] { "conversation_id", "user_id" });

            migrationBuilder.CreateTable(
                name: "conversation_membership_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_sequence = table.Column<long>(type: "bigint", nullable: false),
                    end_sequence = table.Column<long>(type: "bigint", nullable: true),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    left_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_membership_periods", x => x.id);
                    table.CheckConstraint("ck_period_bounds", "start_sequence >= 0 AND (end_sequence IS NULL OR end_sequence >= start_sequence)");
                    table.ForeignKey(
                        name: "fk_conversation_membership_periods_conversation_members_conver",
                        columns: x => new { x.conversation_id, x.user_id },
                        principalTable: "conversation_members",
                        principalColumns: new[] { "conversation_id", "user_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_operations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_group_active_owner",
                table: "conversation_members",
                column: "conversation_id",
                unique: true,
                filter: "role = 0 AND left_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invite_uses",
                table: "conversation_invites",
                sql: "used_count >= 0 AND (max_uses IS NULL OR (max_uses > 0 AND used_count <= max_uses))");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_membership_periods_conversation_id_user_id",
                table: "conversation_membership_periods",
                columns: new[] { "conversation_id", "user_id" },
                unique: true,
                filter: "end_sequence IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_membership_periods_conversation_id_user_id_sta",
                table: "conversation_membership_periods",
                columns: new[] { "conversation_id", "user_id", "start_sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_group_operations_actor_id_operation_key",
                table: "group_operations",
                columns: new[] { "actor_id", "operation", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_next_attempt_at_created_at",
                table: "outbox_events",
                columns: new[] { "next_attempt_at", "created_at" },
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_membership_periods");

            migrationBuilder.DropTable(
                name: "group_operations");

            migrationBuilder.DropTable(
                name: "outbox_events");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_conversation_members_conversation_id_user_id",
                table: "conversation_members");

            migrationBuilder.DropIndex(
                name: "ix_group_active_owner",
                table: "conversation_members");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invite_uses",
                table: "conversation_invites");

            migrationBuilder.DropColumn(
                name: "close_reason",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "closed_at",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "version",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "last_read_sequence",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "leave_reason",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "left_at_sequence",
                table: "conversation_members");

            migrationBuilder.DropColumn(
                name: "removed_by_user_id",
                table: "conversation_members");

            migrationBuilder.RenameColumn(
                name: "invite_code_hash",
                table: "conversation_invites",
                newName: "invite_code");

            migrationBuilder.RenameIndex(
                name: "ix_conversation_invites_invite_code_hash",
                table: "conversation_invites",
                newName: "ix_conversation_invites_invite_code");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_members_conversation_id",
                table: "conversation_members",
                column: "conversation_id");
        }
    }
}
