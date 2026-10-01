#!/bin/sh
set -eu
# Set PGHOST/PGPORT/PGUSER/PGDATABASE and BACKUP_PASSPHRASE in a private environment.
: "${BACKUP_PASSPHRASE:?Set a private backup passphrase}"
: "${PGDATABASE:?Set the database name}"
task_backup_dir="${BACKUP_DIR:-.local/backups}"
mkdir -p "$task_backup_dir"
umask 077
task_backup_path="$task_backup_dir/learning-$(date -u +%Y%m%dT%H%M%SZ).pgdump.gpg"
task_plain=$(mktemp)
trap 'rm -f "$task_plain"' EXIT INT TERM
pg_dump --format=custom --no-owner --file="$task_plain"
printf '%s' "$BACKUP_PASSPHRASE" | gpg --batch --yes --pinentry-mode loopback --passphrase-fd 0 --symmetric --cipher-algo AES256 --output "$task_backup_path" "$task_plain"
echo "Backup saved: $task_backup_path"
