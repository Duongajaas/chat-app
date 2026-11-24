using ChatApp.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ChatApp.Migrations;

// Recent models were committed without their matching migrations. Add only missing schema;
// deployments that already created these objects retain their data and existing definitions.
[DbContext(typeof(AppDbContext))]
[Migration("20261115090000_EnsureRecentChatSchema")]
public sealed class EnsureRecentChatSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE conversation_members ADD COLUMN IF NOT EXISTS request_status integer NOT NULL DEFAULT 1;

            ALTER TABLE message_attachments ADD COLUMN IF NOT EXISTS storage_key text NOT NULL DEFAULT '';
            ALTER TABLE message_attachments ADD COLUMN IF NOT EXISTS thumbnail_key text;
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'message_attachments' AND column_name = 'file_url') THEN
                    ALTER TABLE message_attachments ALTER COLUMN file_url SET DEFAULT '';
                END IF;
            END $$;
            ALTER TABLE notifications ADD COLUMN IF NOT EXISTS channel text NOT NULL DEFAULT 'in_app';
            ALTER TABLE notifications ADD COLUMN IF NOT EXISTS last_attempt_at timestamp with time zone;
            ALTER TABLE notifications ADD COLUMN IF NOT EXISTS last_error text;
            ALTER TABLE notifications ADD COLUMN IF NOT EXISTS retry_count integer NOT NULL DEFAULT 0;
            ALTER TABLE notifications ADD COLUMN IF NOT EXISTS sent_at timestamp with time zone;
            ALTER TABLE notifications ADD COLUMN IF NOT EXISTS status integer NOT NULL DEFAULT 0;

            CREATE TABLE IF NOT EXISTS friend_links (
                id uuid NOT NULL PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                token_hash character varying(128) NOT NULL,
                token character varying(64) NOT NULL,
                expires_at timestamp with time zone,
                max_uses integer,
                used_count integer NOT NULL,
                revoked_at timestamp with time zone,
                created_at timestamp with time zone NOT NULL
            );

            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'messages' AND column_name = 'sequence') THEN
                    ALTER TABLE messages ADD COLUMN sequence bigint;
                    WITH ordered AS (SELECT id, row_number() OVER (ORDER BY created_at, id) AS sequence FROM messages)
                    UPDATE messages AS m SET sequence = ordered.sequence FROM ordered WHERE m.id = ordered.id;
                    ALTER TABLE messages ALTER COLUMN sequence SET NOT NULL;
                    ALTER TABLE messages ALTER COLUMN sequence ADD GENERATED ALWAYS AS IDENTITY;
                    PERFORM setval(pg_get_serial_sequence('messages', 'sequence'),
                        GREATEST(COALESCE((SELECT MAX(sequence) FROM messages), 1), 1), EXISTS(SELECT 1 FROM messages));
                END IF;
            END $$;

            CREATE UNIQUE INDEX IF NOT EXISTS ix_friend_links_token_hash ON friend_links(token_hash);
            CREATE INDEX IF NOT EXISTS ix_friend_links_user_id ON friend_links(user_id);
            CREATE INDEX IF NOT EXISTS ix_messages_conversation_id_sequence ON messages(conversation_id, sequence);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_messages_sender_id_client_message_id ON messages(sender_id, client_message_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_user_devices_user_id_device_token ON user_devices(user_id, device_token);

            ALTER TABLE messages ADD COLUMN IF NOT EXISTS call_session_id uuid;

            ALTER TABLE messages ADD COLUMN IF NOT EXISTS is_forwarded boolean NOT NULL DEFAULT FALSE;

            ALTER TABLE messages ADD COLUMN IF NOT EXISTS media_url text;

            ALTER TABLE messages ADD COLUMN IF NOT EXISTS request_hash text;

            ALTER TABLE messages ADD COLUMN IF NOT EXISTS voice_duration integer;

            ALTER TABLE messages ADD COLUMN IF NOT EXISTS waveform_points text;

            ALTER TABLE message_mentions ADD COLUMN IF NOT EXISTS length integer NOT NULL DEFAULT 0;

            ALTER TABLE message_mentions ADD COLUMN IF NOT EXISTS start integer NOT NULL DEFAULT 0;

            ALTER TABLE message_mentions ADD COLUMN IF NOT EXISTS username text NOT NULL DEFAULT '';

            ALTER TABLE conversations ADD COLUMN IF NOT EXISTS avatar_asset_id uuid;

            ALTER TABLE call_sessions ADD COLUMN IF NOT EXISTS client_call_id uuid;

            ALTER TABLE call_sessions ADD COLUMN IF NOT EXISTS connect_deadline_at timestamp with time zone;

            ALTER TABLE call_sessions ADD COLUMN IF NOT EXISTS connected_at timestamp with time zone;

            ALTER TABLE call_sessions ADD COLUMN IF NOT EXISTS ended_reason text;

            ALTER TABLE call_sessions ADD COLUMN IF NOT EXISTS expires_at timestamp with time zone;

            ALTER TABLE call_sessions ADD COLUMN IF NOT EXISTS negotiation_epoch integer NOT NULL DEFAULT 0;

            ALTER TABLE call_sessions ADD COLUMN IF NOT EXISTS recovery_deadline_at timestamp with time zone;

            ALTER TABLE call_sessions ADD COLUMN IF NOT EXISTS request_hash text;

            ALTER TABLE call_sessions ADD COLUMN IF NOT EXISTS restart_count integer NOT NULL DEFAULT 0;

            ALTER TABLE call_sessions ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;

            ALTER TABLE call_participants ADD COLUMN IF NOT EXISTS auth_session_id uuid;

            ALTER TABLE call_participants ADD COLUMN IF NOT EXISTS connection_id text;

            ALTER TABLE call_participants ADD COLUMN IF NOT EXISTS credential_hash text;

            ALTER TABLE call_participants ADD COLUMN IF NOT EXISTS endpoint_generation bigint NOT NULL DEFAULT 0;

            ALTER TABLE call_participants ADD COLUMN IF NOT EXISTS endpoint_id uuid;

            ALTER TABLE call_participants ADD COLUMN IF NOT EXISTS last_heartbeat_at timestamp with time zone;

            ALTER TABLE call_participants ADD COLUMN IF NOT EXISTS lease_expires_at timestamp with time zone;

            ALTER TABLE call_participants ADD COLUMN IF NOT EXISTS media_connected_at timestamp with time zone;

            CREATE TABLE IF NOT EXISTS active_call_slots (
                user_id uuid NOT NULL,
                call_session_id uuid NOT NULL,
                CONSTRAINT pk_active_call_slots PRIMARY KEY (user_id),
                CONSTRAINT fk_active_call_slots_call_sessions_call_session_id FOREIGN KEY (call_session_id) REFERENCES call_sessions (id) ON DELETE CASCADE,
                CONSTRAINT fk_active_call_slots_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS avatar_assets (
                id uuid NOT NULL,
                conversation_id uuid NOT NULL,
                uploaded_by uuid NOT NULL,
                public_id text NOT NULL,
                provider_asset_id text,
                delivery_url text,
                provider_version bigint,
                created_at timestamp with time zone NOT NULL,
                expires_at timestamp with time zone NOT NULL,
                verified_at timestamp with time zone,
                attached_at timestamp with time zone,
                delete_after timestamp with time zone,
                delete_pending boolean NOT NULL,
                deleted_at timestamp with time zone,
                CONSTRAINT pk_avatar_assets PRIMARY KEY (id),
                CONSTRAINT fk_avatar_assets_conversations_conversation_id FOREIGN KEY (conversation_id) REFERENCES conversations (id) ON DELETE CASCADE,
                CONSTRAINT fk_avatar_assets_users_uploaded_by FOREIGN KEY (uploaded_by) REFERENCES users (id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS block_cooldowns (
                actor_id uuid NOT NULL,
                target_id uuid NOT NULL,
                last_unblocked_at timestamp with time zone NOT NULL,
                reblock_allowed_at timestamp with time zone NOT NULL,
                CONSTRAINT pk_block_cooldowns PRIMARY KEY (actor_id, target_id),
                CONSTRAINT fk_block_cooldowns_users_actor_id FOREIGN KEY (actor_id) REFERENCES users (id) ON DELETE CASCADE,
                CONSTRAINT fk_block_cooldowns_users_target_id FOREIGN KEY (target_id) REFERENCES users (id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS private_media_assets (
                id uuid NOT NULL,
                owner_id uuid NOT NULL,
                storage_key character varying(100) NOT NULL,
                file_name character varying(200) NOT NULL,
                content_type character varying(100) NOT NULL,
                length bigint NOT NULL,
                uploaded boolean NOT NULL,
                created_at timestamp with time zone NOT NULL,
                CONSTRAINT pk_private_media_assets PRIMARY KEY (id),
                CONSTRAINT fk_private_media_assets_users_owner_id FOREIGN KEY (owner_id) REFERENCES users (id) ON DELETE RESTRICT
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ix_messages_call_session_id ON messages (call_session_id);

            CREATE INDEX IF NOT EXISTS ix_conversations_avatar_asset_id ON conversations (avatar_asset_id);

            CREATE UNIQUE INDEX IF NOT EXISTS ix_call_sessions_caller_id_client_call_id ON call_sessions (caller_id, client_call_id);

            CREATE INDEX IF NOT EXISTS ix_call_sessions_status_connect_deadline_at ON call_sessions (status, connect_deadline_at);

            CREATE INDEX IF NOT EXISTS ix_call_sessions_status_expires_at ON call_sessions (status, expires_at);

            CREATE INDEX IF NOT EXISTS ix_active_call_slots_call_session_id ON active_call_slots (call_session_id);

            CREATE INDEX IF NOT EXISTS ix_avatar_assets_conversation_id ON avatar_assets (conversation_id);

            CREATE INDEX IF NOT EXISTS ix_avatar_assets_deleted_at_created_at ON avatar_assets (deleted_at, created_at);

            CREATE UNIQUE INDEX IF NOT EXISTS ix_avatar_assets_public_id ON avatar_assets (public_id);

            CREATE INDEX IF NOT EXISTS ix_avatar_assets_uploaded_by ON avatar_assets (uploaded_by);

            CREATE INDEX IF NOT EXISTS ix_block_cooldowns_target_id ON block_cooldowns (target_id);

            CREATE INDEX IF NOT EXISTS ix_private_media_assets_owner_id_created_at ON private_media_assets (owner_id, created_at);

            CREATE UNIQUE INDEX IF NOT EXISTS ix_private_media_assets_storage_key ON private_media_assets (storage_key);

            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_conversations_avatar_assets_avatar_asset_id' AND conrelid = 'conversations'::regclass) THEN
                    ALTER TABLE conversations ADD CONSTRAINT fk_conversations_avatar_assets_avatar_asset_id FOREIGN KEY (avatar_asset_id) REFERENCES avatar_assets (id) ON DELETE SET NULL;
                END IF;
            END $$;
            """);

    // These tables and columns may predate this repair and contain user data.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
