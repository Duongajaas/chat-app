#!/usr/bin/env bash
set -euo pipefail
: "${COMPOSE_ENV_FILE:?Set COMPOSE_ENV_FILE to the production env file}"
backup_dir="${BACKUP_DIRECTORY:?Set BACKUP_DIRECTORY outside the repository}"
mkdir -p "$backup_dir"
chmod 700 "$backup_dir"
backup_file="$backup_dir/chatapp-$(date -u +%Y%m%dT%H%M%SZ).dump"
umask 077
docker compose --env-file "$COMPOSE_ENV_FILE" -f Docker-compose.yml exec -T db \
  sh -c 'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$backup_file.partial"
mv "$backup_file.partial" "$backup_file"
printf 'Backup saved: %s\n' "$backup_file"
