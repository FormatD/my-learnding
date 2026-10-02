#!/usr/bin/env python3
"""Generate the actual API contract in a migrated disposable database, then compare."""
import argparse
import getpass
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from openapi_contract import canonical, changes
import api_acceptance
from api_acceptance import Client
from success_response_contract import verify
from failure_response_contract import verify as verify_failures, verify_rate
from budget_reporting_acceptance import verify as verify_budget
from resource_revision_acceptance import verify as verify_resources
from mapping_suggestion_acceptance import verify as verify_mappings
from content_review_acceptance import verify as verify_content_reviews
from published_mapping_acceptance import verify as verify_published_mappings
from learning_reference_acceptance import verify as verify_learning_references
from independent_mapping_acceptance import verify as verify_independent_mappings
from evidence_revocation_acceptance import verify as verify_revocations


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--export", type=Path, help="Explicitly export contract instead of checking saved snapshot.")
    parser.add_argument("--baseline", type=Path, help="Also compare a previous revision's contract.")
    parser.add_argument("--regression", action="store_true", help="Run core API, version/PDF, content authoring and observed-step suites in the same disposable service.")
    parser.add_argument("--export-schema", action="store_true", help="Explicitly regenerate the schema dictionary from this migrated disposable database.")
    parser.add_argument("--paper-regression",action="store_true",help="Run paper confirmation in a separate disposable service from core regression (keeps real login limit intact).")
    args = parser.parse_args()
    if args.paper_regression and args.regression:parser.error("Run paper regression separately from core regression to respect the real login limit.")
    env = os.environ.copy()
    env["PATH"] = "/opt/homebrew/opt/postgresql@16/bin:" + env["PATH"]
    database = "learning_fault_openapi_" + uuid.uuid4().hex[:12]
    env.update(PGHOST=env.get("PGHOST", "127.0.0.1"), PGPORT=env.get("PGPORT", "55432"),
               PGUSER=env.get("PGUSER", getpass.getuser()), PGDATABASE=database)
    def quoted(value):
        return '"' + value.replace('"', '""') + '"'
    connection = ";".join(f"{key}={quoted(value)}" for key, value in {
        "Host": env["PGHOST"], "Port": env["PGPORT"], "Database": database, "Username": env["PGUSER"]}.items())
    if env.get("PGPASSWORD"):
        connection += ";Password=" + quoted(env["PGPASSWORD"])
    env["ConnectionStrings__Learning"] = connection
    dotnet = str(ROOT / ".tools/dotnet/dotnet") if (ROOT / ".tools/dotnet/dotnet").exists() else shutil.which("dotnet")
    assert dotnet, "Build requires .NET SDK."
    child = None
    created = False
    with tempfile.TemporaryDirectory(prefix="learning-openapi-") as directory:
        temp = Path(directory)
        env.update(DeletionLedger=str(temp / "deleted-students.txt"),
                   FamilyDeletionLedger=str(temp / "deleted-families.txt"), ExportDirectory=str(temp / "exports"))
        env.pop("BackupConfigFile", None)
        with (temp / "process.log").open("w+") as log:
            try:
                subprocess.run(["createdb", database], env=env, check=True, stdout=log, stderr=log, timeout=30)
                created = True
                with socket.socket() as listener:
                    listener.bind(("127.0.0.1", 0))
                    port = listener.getsockname()[1]
                origin = f"http://127.0.0.1:{port}"
                child = subprocess.Popen([dotnet, str(ROOT / "src/server/bin/Debug/net10.0/Learning.Api.dll"),
                                          "--urls", origin], cwd=ROOT / "src/server", env=env,
                                         stdout=log, stderr=log)
                deadline = time.monotonic() + 40
                while time.monotonic() < deadline:
                    assert child.poll() is None, "Disposable API stopped; no production database was used."
                    try:
                        with urllib.request.urlopen(origin + "/api/health", timeout=1) as response:
                            if response.status == 200:
                                break
                    except OSError:
                        time.sleep(.1)
                else:
                    raise AssertionError("Disposable API did not become ready.")
                api_acceptance.BASE = origin + "/api/v1"
                client = Client()
                registered=client.request("/auth/register", {"userName": "contract-" + uuid.uuid4().hex[:12],
                               "password": secrets.token_hex(24)}, expected=201)
                document = canonical(client.request("/openapi.json"))
                assert document["openapi"].startswith("3.") and document["paths"]
                schemas = document["components"]["schemas"]
                assert "WeeklySummary" in schemas and "ParentBurdenSummary" in schemas
                verify(document,client,registered)
                verify_budget(document,client)
                verify_resources(document,client)
                verify_mappings(document,client)
                verify_content_reviews(document,client)
                verify_published_mappings(document,client)
                verify_revocations(document,client)
                verify_independent_mappings(document,client)
                verify_learning_references(document,client,env)
                encoded = json.dumps(document, ensure_ascii=False, sort_keys=True, indent=2) + "\n"
                if args.export:
                    args.export.parent.mkdir(parents=True, exist_ok=True)
                    args.export.write_text(encoded)
                    print(f"Exported actual API: {len(document['paths'])} paths / {len(schemas)} schemas.")
                else:
                    snapshot = ROOT / "docs/api/openapi.json"
                    assert snapshot.read_text() == encoded, "Actual OpenAPI differs from saved contract; review and explicitly regenerate."
                    print(f"PASS actual migrated API matches saved contract: {len(document['paths'])} paths / {len(schemas)} schemas")
                if args.baseline:
                    findings = changes(json.loads(args.baseline.read_text()), document)
                    assert not findings, "Potentially incompatible API changes:\n" + "\n".join(findings)
                    print("PASS previous revision compatibility gate")
                subprocess.run([sys.executable, str(ROOT / "scripts/schema_dictionary.py"), *([] if args.export_schema else ["--check"])],
                               env=env, check=True, timeout=50)
                print("PASS current migrations produce the documented schema in an empty database")
                if args.regression:
                    import advanced_api_acceptance, content_authoring_api_acceptance, observed_steps_api_acceptance
                    api_acceptance.main()
                    advanced_api_acceptance.main()
                    content_authoring_api_acceptance.main()
                    observed_steps_api_acceptance.main()
                if args.paper_regression:
                    import paper_learning_api_acceptance
                    paper_learning_api_acceptance.main()
                verify_failures(document,client,env)
                verify_rate(document)
            finally:
                if child is not None and child.poll() is None:
                    child.terminate()
                    try:
                        child.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        child.kill()
                        child.wait(timeout=5)
                if created:
                    subprocess.run(["dropdb", "--if-exists", database], env=env, check=True,
                                   stdout=log, stderr=log, timeout=30)


if __name__ == "__main__":
    main()
