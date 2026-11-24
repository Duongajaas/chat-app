using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatApp.Migrations
{
    public partial class RepairRefreshTokenSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "device_id", table: "refresh_tokens", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(
                name: "family_id", table: "refresh_tokens", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<DateTime>(
                name: "used_at", table: "refresh_tokens", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by_token_id", table: "refresh_tokens", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "revocation_reason", table: "refresh_tokens", type: "text", nullable: true);

            // Existing tokens start separate families; preserve their data and validity.
            migrationBuilder.Sql("UPDATE refresh_tokens SET family_id = id;");
            migrationBuilder.AlterColumn<Guid>(
                name: "family_id", table: "refresh_tokens", type: "uuid", nullable: false,
                oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_device_id", table: "refresh_tokens", column: "device_id");
            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expires_at", table: "refresh_tokens", column: "expires_at");
            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id_family_id", table: "refresh_tokens",
                columns: new[] { "user_id", "family_id" });
            migrationBuilder.AddForeignKey(
                name: "fk_refresh_tokens_user_devices_device_id", table: "refresh_tokens",
                column: "device_id", principalTable: "user_devices", principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_refresh_tokens_user_devices_device_id", table: "refresh_tokens");
            migrationBuilder.DropIndex(name: "ix_refresh_tokens_device_id", table: "refresh_tokens");
            migrationBuilder.DropIndex(name: "ix_refresh_tokens_expires_at", table: "refresh_tokens");
            migrationBuilder.DropIndex(name: "ix_refresh_tokens_user_id_family_id", table: "refresh_tokens");
            migrationBuilder.DropColumn(name: "device_id", table: "refresh_tokens");
            migrationBuilder.DropColumn(name: "family_id", table: "refresh_tokens");
            migrationBuilder.DropColumn(name: "used_at", table: "refresh_tokens");
            migrationBuilder.DropColumn(name: "replaced_by_token_id", table: "refresh_tokens");
            migrationBuilder.DropColumn(name: "revocation_reason", table: "refresh_tokens");
        }
    }
}
