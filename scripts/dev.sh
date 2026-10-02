#!/bin/sh
set -eu
umask 077
task_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$task_root"
if test -x .tools/dotnet/dotnet; then PATH="$task_root/.tools/dotnet:$PATH"; export PATH; DOTNET_ROOT="$task_root/.tools/dotnet"; export DOTNET_ROOT; fi
if test -d /opt/homebrew/opt/postgresql@16/bin; then PATH="/opt/homebrew/opt/postgresql@16/bin:$PATH"; export PATH; fi
if test -z "${ConnectionStrings__Learning:-}"; then
  mkdir -p .local
  if ! test -d .local/postgres; then initdb -D .local/postgres -A trust --no-locale -E UTF8; fi
  if ! pg_isready -h 127.0.0.1 -p 55432 >/dev/null; then pg_ctl -D .local/postgres -l .local/postgres.log -o '-h 127.0.0.1 -p 55432' start; fi
  ConnectionStrings__Learning="Host=127.0.0.1;Port=55432;Database=learning;Username=$(id -un)"; export ConnectionStrings__Learning
fi
if test -f "$task_root/.local/daily-backup-config.json"; then BackupConfigFile="${BackupConfigFile:-$task_root/.local/daily-backup-config.json}"; export BackupConfigFile; fi
DOTNET_CLI_TELEMETRY_OPTOUT=1; export DOTNET_CLI_TELEMETRY_OPTOUT
npm ci --prefix src/web
npm run build --prefix src/web
dotnet run --project src/server --urls http://127.0.0.1:5080
