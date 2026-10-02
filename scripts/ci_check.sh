#!/bin/sh
set -eu
task_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$task_root"
sh scripts/check.sh
python3 tests/openapi_contract_acceptance.py
if test "$#" -gt 0; then
    python3 tests/openapi_acceptance.py --baseline "$1"
else
    python3 tests/openapi_acceptance.py
fi
