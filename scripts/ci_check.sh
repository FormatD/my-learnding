#!/bin/sh
set -eu
task_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$task_root"
sh scripts/check.sh
if test -x .tools/dotnet/dotnet; then .tools/dotnet/dotnet build tests/persistence --no-restore; else dotnet build tests/persistence --no-restore; fi
python3 tests/openapi_contract_acceptance.py
if test "$#" -gt 0; then
    python3 tests/openapi_acceptance.py --baseline "$1"
else
    python3 tests/openapi_acceptance.py
fi

# Queued mapping requests use a separate service to retain the real authentication limit.
python3 tests/openapi_acceptance.py --mapping-regression
python3 tests/mapping_job_persistence_acceptance.py
python3 tests/mapping_job_fault_acceptance.py
python3 tests/mapping_compatibility_acceptance.py
python3 tests/mapping_compatibility_api_acceptance.py

# Manual assessment rebuilds retain real authentication limits in an isolated service.
python3 tests/openapi_acceptance.py --rebuild-regression
python3 tests/rebuild_job_persistence_acceptance.py
python3 tests/rebuild_compatibility_acceptance.py
python3 tests/rebuild_compatibility_api_acceptance.py
python3 tests/builder_candidate_review_acceptance.py
python3 tests/unit_pack_service_acceptance.py
python3 tests/builder_retrieval_acceptance.py
python3 tests/kc_descriptions_api_acceptance.py
python3 tests/builder_retrieval_api_acceptance.py
python3 tests/rebuild_job_fault_acceptance.py

# Cancellation uses an independent job-control transaction and an actual running worker.
python3 tests/openapi_acceptance.py --cancel-regression
python3 tests/job_cancellation_acceptance.py

# Stateful incremental transitions, immutable persistence and suffix crash recovery.
python3 tests/incremental_checkpoint_persistence_acceptance.py
python3 tests/incremental_checkpoint_fault_acceptance.py
python3 tests/review_rule_upgrade_acceptance.py
python3 tests/review_target_confirmation_acceptance.py
python3 tests/review_target_confirmation_api_acceptance.py
python3 tests/review_target_confirmation_fault_acceptance.py

# Online active-generation append and real rollback/recovery.
python3 tests/online_assessment_acceptance.py
python3 tests/online_assessment_fault_acceptance.py

# Actual ordered source receipt progress and crash atomicity.
python3 tests/assessment_consumption_acceptance.py
python3 tests/assessment_consumption_fault_acceptance.py

# Bound actual materialized sources to referenced records without changing input bytes.
python3 tests/assessment_source_loading_acceptance.py

# Capture real plan input progress and reject stale adaptive recommendations.
python3 tests/plan_projection_acceptance.py
