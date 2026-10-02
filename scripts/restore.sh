#!/bin/sh
set -eu
: "${BACKUP_PASSPHRASE:?Set the backup passphrase}"
: "${PGDATABASE:?Set a NEW empty database name}"
: "${DELETION_LEDGER:?Provide the independent deletion ledger}"
test -f "$DELETION_LEDGER"
task_family_ledger=${FAMILY_DELETION_LEDGER:-"$(dirname "$DELETION_LEDGER")/deleted-families.txt"}
test -f "$task_family_ledger" || { echo "Missing independent family deletion ledger" >&2; exit 1; }
task_plain=$(mktemp)
trap 'rm -f "$task_plain"' EXIT INT TERM
printf '%s' "$BACKUP_PASSPHRASE" | gpg --batch --yes --pinentry-mode loopback --passphrase-fd 0 --decrypt --output "$task_plain" "$1"
# No --clean: refuse accidental overwrite of an existing database schema.
pg_restore --exit-on-error --no-owner --dbname="$PGDATABASE" "$task_plain"
# Restore starts with fresh logins and fresh transport command caches.
psql --set=ON_ERROR_STOP=1 <<'SQL'
DELETE FROM "AuthSessions";
DELETE FROM "Commands";
SQL
while IFS=, read -r task_deleted_family task_receipt; do
  case "$task_deleted_family,$task_receipt" in *[!0-9a-f,-]*) echo 'Invalid family deletion ledger' >&2; exit 1;; esac
  test -n "$task_deleted_family" || continue
  psql --set=ON_ERROR_STOP=1 --set=family="$task_deleted_family" <<'SQL'
BEGIN;
SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Families' AND column_name='OwnerAccountId') AS family_has_owner \gset
\if :family_has_owner
UPDATE "Families" SET "OwnerAccountId"=NULL WHERE "Id"=:'family'::uuid;
\endif
DELETE FROM "Families" WHERE "Id"=:'family'::uuid;
COMMIT;
SQL
done < "$task_family_ledger"
while IFS=, read -r task_family_id task_student_id; do
  case "$task_family_id,$task_student_id" in *[!0-9a-f,-]*) echo 'Invalid deletion ledger' >&2; exit 1;; esac
  test -n "$task_student_id" || continue
  psql --set=ON_ERROR_STOP=1 --set=student="$task_student_id" --set=family="$task_family_id" <<'SQL'
DELETE FROM "PrivateFile" f
WHERE f."FamilyId" = :'family'::uuid
  AND f."Id" IN (SELECT p."FileId" FROM "PaperWrong" p WHERE p."StudentId" = :'student'::uuid)
  AND NOT EXISTS (SELECT 1 FROM "PaperWrong" p WHERE p."FileId" = f."Id" AND p."StudentId" <> :'student'::uuid)
  AND NOT EXISTS (SELECT 1 FROM "Sources" s WHERE s."FamilyId" = :'family'::uuid AND s."Hash" = f."Hash");
DELETE FROM "Audits" a WHERE a."FamilyId"=:'family'::uuid AND (
  (CASE WHEN a."Action"='TaskTransition' THEN a."Details"::jsonb ELSE '{}'::jsonb END)->>'studentId'=:'student'
  OR (CASE WHEN a."Action"='PlanAdjusted' THEN a."Details"::jsonb ELSE '{}'::jsonb END)->>'planId'
     IN (SELECT p."Id"::text FROM "Plans" p WHERE p."FamilyId"=:'family'::uuid AND p."StudentId"=:'student'::uuid));
DELETE FROM "Students" WHERE "Id" = :'student'::uuid AND "FamilyId" = :'family'::uuid;
DELETE FROM "AuthSessions" WHERE "StudentId" = :'student'::uuid AND "FamilyId" = :'family'::uuid;
DELETE FROM "Commands" WHERE "FamilyId" = :'family'::uuid;
SQL
done < "$DELETION_LEDGER"
echo 'Restore completed; deletion ledger reapplied.'
