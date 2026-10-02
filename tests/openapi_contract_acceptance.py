#!/usr/bin/env python3
"""Mutate the real contract to prove the compatibility gate rejects relevant breakages."""
from copy import deepcopy
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from openapi_contract import canonical, changes


def main():
    original = json.loads((ROOT / "docs/api/openapi.json").read_text())
    endpoint = "/api/v1/students/{id}/weekly-summary"
    assert not changes(original, deepcopy(original))
    altered = deepcopy(original)
    altered["servers"] = [{"url": "http://127.0.0.1:9999"}]
    altered["paths"][endpoint]["get"]["description"] = "New explanatory text"
    assert not changes(original, altered)
    print("PASS deterministic comparison ignores listener host and documentation annotations")

    for action in ("path", "method", "field", "media"):
        altered = deepcopy(original)
        if action == "path":
            del altered["paths"][endpoint]
        elif action == "method":
            del altered["paths"][endpoint]["get"]
        elif action == "field":
            del altered["components"]["schemas"]["Credentials"]["properties"]["password"]
        else:
            del altered["paths"][endpoint]["get"]["responses"]["200"]["content"]["application/json"]
        assert changes(original, altered), action
    print("PASS existing route, method, request field and response media removal rejected")

    altered = deepcopy(original)
    altered["components"]["schemas"]["Credentials"]["properties"]["password"]["type"] = "integer"
    assert any("password/type" in d for d in changes(original, altered))
    altered = deepcopy(original)
    altered["paths"][endpoint]["get"]["parameters"].append({"name": "must", "in": "query", "required": True, "schema": {"type": "string"}})
    assert any("new required parameter" in d for d in changes(original, altered))
    altered = deepcopy(original)
    altered["components"]["schemas"]["Credentials"].setdefault("required", []).append("newField")
    assert changes(original, altered)
    print("PASS field type and new required query/property changes rejected")

    altered = deepcopy(original)
    altered["paths"]["/api/v1/new-endpoint"] = {"get": {"responses": {"200": {}}}}
    altered["paths"][endpoint]["get"]["parameters"].append({"name": "optional", "in": "query", "schema": {"type": "string"}})
    altered["components"]["schemas"]["Credentials"]["properties"]["optional"] = {"type": "string"}
    altered["components"]["schemas"]["NewSchema"] = {"type": "object"}
    assert not changes(original, altered)
    print("PASS additive endpoint, schema and optional parameter/property allowed")

    for new_value in ({"security": [{"auth": []}]}, {"enum": ["NewValue"]}, {"maxLength": 2}):
        altered = deepcopy(original)
        altered["components"]["schemas"]["Credentials"]["properties"]["password"].update(new_value)
        assert changes(original, altered)
    altered = deepcopy(original)
    altered["components"]["schemas"]["Credentials"]["properties"]["password"] = {"$ref": "#/components/schemas/StudentInput"}
    assert changes(original, altered)
    print("PASS uncertain security, enum, validation constraint and reference edits require review")

    special = {"components": {"schemas": {"description": {"type": "object", "properties": {"summary": {"type": "string"}, "description": {"type": "string"}}}}}}
    altered = deepcopy(special)
    del altered["components"]["schemas"]["description"]["properties"]["summary"]
    assert changes(special, altered)
    assert "description" in canonical(special)["components"]["schemas"]
    security = {"security": [{"description": ["read"]}]}
    altered = deepcopy(security)
    altered["security"][0]["description"] = ["write"]
    assert changes(security, altered)
    print("PASS fields/schema identities named summary/description are retained and protected")

    tuple_schema = {"components": {"schemas": {"Tuple": {"prefixItems": [{"type": "string"}, {"type": "integer"}]}}}}
    altered = deepcopy(tuple_schema)
    altered["components"]["schemas"]["Tuple"]["prefixItems"].reverse()
    assert changes(tuple_schema, altered)
    print("PASS positional array order change rejected")


if __name__ == "__main__":
    main()
