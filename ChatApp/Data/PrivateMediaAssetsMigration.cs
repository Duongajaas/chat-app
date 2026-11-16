using ChatApp.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ChatApp.Migrations;

// Kept outside Migrations/ (git-ignored) so every clone gets it. The model snapshot already contains
// private_media_assets, but no tracked migration created it, so uploads failed with "relation does not exist".
// Idempotent: a no-op on databases where the table/columns were created by an untracked migration.
[DbContext(typeof(AppDbContext))]
[Migration("20261113090000_EnsurePrivateMediaAssets")]
public sealed class EnsurePrivateMediaAssets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS private_media_assets (
                id uuid NOT NULL,
                owner_id uuid NOT NULL,
                storage_key character varying(100) NOT NULL,
                file_name character varying(200) NOT NULL,
                content_type character varying(100) NOT NULL,
                length bigint NOT NULL,
                uploaded boolean NOT NULL,
                created_at timestamp with time zone NOT NULL,
                CONSTRAINT pk_private_media_assets PRIMARY KEY (id)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_private_media_assets_storage_key ON private_media_assets (storage_key);
            CREATE INDEX IF NOT EXISTS ix_private_media_assets_owner_id_created_at ON private_media_assets (owner_id, created_at);
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_private_media_assets_users_owner_id') THEN
                    ALTER TABLE private_media_assets ADD CONSTRAINT fk_private_media_assets_users_owner_id
                        FOREIGN KEY (owner_id) REFERENCES users (id) ON DELETE RESTRICT;
                END IF;
            END $$;
            """);
    }

    // Intentionally empty: the table may predate this migration on shared databases and holds user uploads.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
