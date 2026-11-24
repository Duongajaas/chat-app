using ChatApp.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ChatApp.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261116090000_ExpressionMessages")]
public sealed class ExpressionMessages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE messages ADD COLUMN IF NOT EXISTS expression_json jsonb NULL;");

    // Keep sent expressions if an older application version is rolled back.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
