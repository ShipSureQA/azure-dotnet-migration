#!/usr/bin/env python3
"""Record golden responses from the LEGACY PHP API (the source of truth).

Usage:
  ENABLE_TEST_ENDPOINTS=true php -S 127.0.0.1:8080 legacy-php/public/index.php &
  python3 tools/record_golden.py [--base-url http://127.0.0.1:8080]

Writes tests/Orders.ParityTests/Contracts/golden/<case>.json for every case in
parity-cases.json. Volatile fields (createdAt of new orders) are replaced by a placeholder.
Only re-record when the legacy behaviour is meant to change, and review the diff.
"""
import argparse
import json
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONTRACTS = ROOT / "tests" / "Orders.ParityTests" / "Contracts"
TIMESTAMP = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")


class NoProxy(urllib.request.ProxyHandler):
    def __init__(self):
        super().__init__({})


OPENER = urllib.request.build_opener(NoProxy())


def send(base, method, path, body=None):
    data = body.encode("utf-8") if body is not None else None
    req = urllib.request.Request(base + path, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with OPENER.open(req, timeout=10) as resp:
            return resp.status, dict(resp.headers), resp.read().decode("utf-8")
    except urllib.error.HTTPError as err:
        return err.code, dict(err.headers), err.read().decode("utf-8")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://127.0.0.1:8080")
    args = parser.parse_args()

    cases = json.loads((CONTRACTS / "parity-cases.json").read_text(encoding="utf-8"))
    out_dir = CONTRACTS / "golden"
    out_dir.mkdir(parents=True, exist_ok=True)

    for case in cases:
        status, _, _ = send(args.base_url, "POST", "/api/test/reset")
        if status != 204:
            sys.exit(f"reset failed with {status}; start PHP with ENABLE_TEST_ENDPOINTS=true")

        status, headers, body = send(args.base_url, case["method"], case["path"], case.get("body"))
        golden = {"status": status}
        wanted = case.get("headers") or []
        if wanted:
            lower = {k.lower(): v for k, v in headers.items()}
            golden["headers"] = {h: lower[h.lower()] for h in wanted}
        if body.strip():
            parsed = json.loads(body)
            for field in case.get("volatile") or []:
                if not TIMESTAMP.match(parsed.get(field, "")):
                    sys.exit(f"{case['name']}: {field} is not a UTC timestamp: {parsed.get(field)!r}")
                parsed[field] = "<timestamp>"
            golden["body"] = parsed

        path = out_dir / f"{case['name']}.json"
        path.write_text(json.dumps(golden, indent=2) + "\n", encoding="utf-8")
        print(f"{status}  {case['name']}")

    send(args.base_url, "POST", "/api/test/reset")


if __name__ == "__main__":
    main()
