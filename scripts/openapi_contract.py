#!/usr/bin/env python3
"""Conservative OpenAPI compatibility gate; uncertain structural edits require review."""
import argparse
import json
from pathlib import Path
import sys

METHODS = {"get", "put", "post", "delete", "patch", "head", "options", "trace"}
ANNOTATIONS = {"description", "summary", "example", "examples", "externalDocs"}
NAMED_MAPS = {"properties", "schemas", "paths", "responses", "content", "headers", "securitySchemes",
              "$defs", "definitions", "links", "callbacks", "dependentSchemas", "patternProperties", "security"}


def normalize(value, named_map=False):
    if isinstance(value, dict):
        return {k: normalize(v, k in NAMED_MAPS) for k, v in sorted(value.items()) if named_map or k not in ANNOTATIONS}
    if isinstance(value, list):
        return [normalize(v, named_map) for v in value]
    return value


def canonical(document):
    result = normalize(document)
    # Runtime host varies across local and CI disposable listeners.
    result.pop("servers", None)
    return result


def changes(before, after):
    """Allow new endpoints/schemas and optional parameters/properties; reject uncertain edits."""
    old, new = canonical(before), canonical(after)
    findings = []

    def check(left, right, path):
        if left == right:
            return
        if isinstance(left, dict) and isinstance(right, dict):
            for key in left.keys() - right.keys():
                findings.append(f"{path}/{key}: removed")
            for key in right.keys() - left.keys():
                # Adding constraints, defaults, security or required flags may break clients.
                if path.endswith("/properties") or path in ("/paths", "/components/schemas"):
                    continue
                if path.startswith("/paths/") and key in METHODS:
                    continue
                if key == "parameters" and isinstance(right[key], list) and all("$ref" not in p and not p.get("required", False) for p in right[key]):
                    continue
                findings.append(f"{path}/{key}: added structural requirement; review")
            for key in left.keys() & right.keys():
                check(left[key], right[key], path + "/" + key)
            return
        if isinstance(left, list) and isinstance(right, list):
            if path.endswith("/parameters") and all(isinstance(p, dict) and "name" in p and "in" in p for p in left + right):
                old_params = {(p["in"], p["name"]): p for p in left}
                new_params = {(p["in"], p["name"]): p for p in right}
                for identity, param in old_params.items():
                    if identity not in new_params:
                        findings.append(f"{path}/{identity}: removed")
                    else:
                        check(param, new_params[identity], path + "/" + str(identity))
                for identity in new_params.keys() - old_params.keys():
                    if new_params[identity].get("required", False) or "$ref" in new_params[identity]:
                        findings.append(f"{path}/{identity}: new required parameter")
                return
            # Required sets and enum/order/union changes are conservatively reviewed.
            if path.endswith(("/required", "/enum")) and sorted(left, key=str) == sorted(right, key=str):
                return
            if left != right:
                findings.append(f"{path}: changed array contract; review")
            return
        findings.append(f"{path}: changed contract value; review")

    check(old, new, "")
    return sorted(set(findings))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("current", type=Path)
    args = parser.parse_args()
    findings = changes(json.loads(args.baseline.read_text()), json.loads(args.current.read_text()))
    if findings:
        print("Potentially incompatible API changes:")
        for finding in findings:
            print("- " + finding)
        sys.exit(1)
    print("API compatibility gate passed (documented additive changes only).")


if __name__ == "__main__":
    main()
