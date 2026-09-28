#!/usr/bin/env bash
set -euo pipefail
: "${COMPOSE_ENV_FILE:?Set COMPOSE_ENV_FILE}"
backup_file="${1:?Pass a backup dump file}"
restore_database="chatapp_restore_$(date -u +%Y%m%d%H%M%S)"
compose=(docker compose --env-file "$COMPOSE_ENV_FILE" -f Docker-compose.yml)
"${compose[@]}" exec -T db sh -c 'exec createdb -U "$POSTGRES_USER" "$1"' sh "$restore_database"
"${compose[@]}" exec -T db sh -c 'exec pg_restore -U "$POSTGRES_USER" --exit-on-error --no-owner -d "$1"' sh "$restore_database" < "$backup_file"
"${compose[@]}" exec -T db sh -c 'exec psql -U "$POSTGRES_USER" -d "$1" -v ON_ERROR_STOP=1 -c "SELECT COUNT(*) AS messages FROM messages; SELECT COUNT(*) AS users FROM users;"' sh "$restore_database"
printf 'Restored into %s. Verify application behavior against this database before removing it.\n' "$restore_database"
