#!/usr/bin/env python3
"""Verify metadata extraction and safe drift detection against the live local schema."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
PSQL = os.environ.get("PSQL", "/opt/homebrew/opt/postgresql@16/bin/psql")
ENV = {**os.environ, "PGHOST": os.environ.get("PGHOST", "127.0.0.1"),
       "PGPORT": os.environ.get("PGPORT", "55432"), "PGDATABASE": os.environ.get("PGDATABASE", "learning"),
       "PGUSER": os.environ.get("PGUSER", "qianjundeng")}


def run(output, *extra, client=PSQL):
    return subprocess.run([sys.executable, str(ROOT / "scripts/schema_dictionary.py"),
                           "--psql", client, "--output-dir", str(output), *extra],
                          env=ENV, text=True, capture_output=True, timeout=50)


def main():
    with tempfile.TemporaryDirectory(prefix="learning-schema-") as temp:
        base = Path(temp)
        docs = base / "docs"
        result = run(docs)
        assert result.returncode == 0, result.stderr
        snapshot = docs / "data/schema.json"
        schema = json.loads(snapshot.read_text())
        tables = {t["name"]: t for t in schema["tables"]}
        assert len(tables) == 46
        assert schema["migrations"][-1]["id"].endswith("StudentScopedAudit")
        audit = tables["Audits"]
        assert next(c for c in audit["columns"] if c["name"] == "StudentId")["nullable"]
        assert any('FOREIGN KEY ("FamilyId", "StudentId")' in c["definition"] and
                   'ON DELETE CASCADE' in c["definition"] for c in audit["constraints"])
        assert any(i["unique"] and '"SupersedesId"' in i["definition"]
                   for i in tables["ParentBurdenRecord"]["indexes"])
        sequence = next(c for c in tables["Attempts"]["columns"] if c["name"] == "Sequence")
        assert sequence["identity"] == "a" and not sequence["nullable"]
        assert all(c["validated"] for t in tables.values() for c in t["constraints"])
        print("PASS actual schema: nullable student audit FK, correction uniqueness, identity, migrations")

        original = {p: p.read_bytes() for p in [snapshot, docs / "data-dictionary.md"]}
        assert run(docs, "--check").returncode == 0
        assert original == {p: p.read_bytes() for p in original}
        changed = json.loads(snapshot.read_text())
        changed["tables"][0]["columns"][0]["type"] = "invented-type"
        snapshot.write_text(json.dumps(changed))
        drift = snapshot.read_bytes()
        result = run(docs, "--check")
        assert result.returncode != 0 and "schema.json" in result.stderr
        assert snapshot.read_bytes() == drift
        assert (docs / "data-dictionary.md").read_bytes() == original[docs / "data-dictionary.md"]
        print("PASS drift is rejected without rewriting either artifact")

        fake = base / "fake-psql"
        fake.write_text("#!/bin/sh\necho 'password=DO_NOT_LOG host=PRIVATE_HOST' >&2\nexit 1\n")
        fake.chmod(0o700)
        result = run(base / "failed", client=str(fake))
        assert result.returncode != 0
        assert "DO_NOT_LOG" not in result.stdout + result.stderr
        assert "PRIVATE_HOST" not in result.stdout + result.stderr
        assert not (base / "failed").exists()
        fake.write_text("#!/bin/sh\ncat <<'METADATA'\n" + json.dumps({"migrations": [], "tables": [{"name": "NewUndocumentedTable"}]}) + "\nMETADATA\n")
        result = run(base / "unknown", client=str(fake))
        assert result.returncode != 0 and not (base / "unknown").exists()
        result = run(base / "missing", client=str(base / "missing-client"))
        assert result.returncode != 0 and not (base / "missing").exists()
        print("PASS connection error redaction, unknown table and missing client fail safely")


if __name__ == "__main__":
    main()
