using ChatApp.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ChatApp.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260928120000_AddMessageMedia")]
public sealed class AddMessageMedia : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "media_url", table: "messages", type: "text", nullable: true);
        migrationBuilder.AddColumn<int>(name: "voice_duration", table: "messages", type: "integer", nullable: true);
        migrationBuilder.AddColumn<string>(name: "waveform_points", table: "messages", type: "text", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "media_url", table: "messages");
        migrationBuilder.DropColumn(name: "voice_duration", table: "messages");
        migrationBuilder.DropColumn(name: "waveform_points", table: "messages");
    }
}
