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
        // Idempotent: some databases already received these columns from AddVoiceStickerMetadata.
        migrationBuilder.Sql("""
            ALTER TABLE messages ADD COLUMN IF NOT EXISTS media_url text;
            ALTER TABLE messages ADD COLUMN IF NOT EXISTS voice_duration integer;
            ALTER TABLE messages ADD COLUMN IF NOT EXISTS waveform_points text;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "media_url", table: "messages");
        migrationBuilder.DropColumn(name: "voice_duration", table: "messages");
        migrationBuilder.DropColumn(name: "waveform_points", table: "messages");
    }
}
