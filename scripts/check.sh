#!/bin/sh
set -eu
task_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$task_root"
if test -x .tools/dotnet/dotnet; then PATH="$task_root/.tools/dotnet:$PATH"; export PATH; fi
npm run build --prefix src/web
dotnet build src/server --no-restore
dotnet run --project tests/acceptance --no-restore
