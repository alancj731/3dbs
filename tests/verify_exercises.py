#!/usr/bin/env python3
"""Checks every exercise against every database through the running API (default http://localhost:5080).

Usage: verify_exercises.py [API_URL] [--insecure]

For each exercise and database:
  * the reference answer passes its own check;
  * a read answer returns at least one row, and a write answer actually changes what its check query sees;
  * an obviously wrong query is graded as incorrect.
Read answers must also return the same rows on all three databases.
Afterwards the row counts must be unchanged, which proves every run was rolled back.
"""
import json, ssl, sys, urllib.request
from decimal import Decimal, ROUND_HALF_EVEN

args = [a for a in sys.argv[1:] if a != "--insecure"]
API = args[0] if args else "http://localhost:5080"
# --insecure: accept a self-signed certificate (e.g. Caddy's local CA for https://localhost)
SSL = ssl._create_unverified_context() if "--insecure" in sys.argv else None
DBS = ["sqlserver", "mysql", "postgres"]

def call(path, body=None):
    req = urllib.request.Request(API + path, data=json.dumps(body).encode() if body is not None else None,
                                 headers={"content-type": "application/json"})
    with urllib.request.urlopen(req, context=SSL) as r:
        return json.load(r)

def norm(v):
    if v is None: return "∅"
    if isinstance(v, bool): return "1" if v else "0"
    if isinstance(v, (int, float)): return format(Decimal(str(v)).quantize(Decimal("0.01"), ROUND_HALF_EVEN).normalize(), "f")
    return str(v).rstrip()

def rows(result): return sorted("|".join(norm(c) for c in r) for r in result["rows"])

COUNTS = "SELECT (SELECT COUNT(*) FROM SalesLT.Customer), (SELECT COUNT(*) FROM SalesLT.Product), (SELECT COUNT(*) FROM SalesLT.ProductCategory), (SELECT COUNT(*) FROM SalesLT.SalesOrderDetail), (SELECT COUNT(*) FROM SalesLT.Address), (SELECT SUM(ListPrice) FROM SalesLT.Product)"
before = {db: rows(call(f"/api/{db}/run", {"sql": COUNTS})["result"]) for db in DBS}

failures = []
exercises = call("/api/exercises")
for ex in exercises:
    expected_by_db = {}
    for db in DBS:
        tag = f"#{ex['id']} {ex['title']} [{db}]"
        ans = call(f"/api/{db}/exercises/{ex['id']}/answer")
        res = call(f"/api/{db}/exercises/{ex['id']}/check", {"sql": ans["sql"]})
        if not res["correct"]:
            failures.append(f"{tag}: own answer not accepted: {res['message']} {res.get('error') or ''}")
            continue
        if ex["kind"] == "read":
            if not res["expected"]["rows"]:
                failures.append(f"{tag}: answer returns no rows")
            # Only the first 500 rows come back for display, and which 500 depends on each
            # database's row order, so only compare complete results across databases.
            if not res["expected"]["truncated"]:
                expected_by_db[db] = rows(res["expected"])
        else:
            untouched = call(f"/api/{db}/run", {"sql": ans["check"]})
            if untouched.get("error"):
                failures.append(f"{tag}: check query fails: {untouched['error']}")
            elif rows(untouched["result"]) == rows(res["expected"]):
                failures.append(f"{tag}: answer doesn't change anything the check query sees")
        wrong = call(f"/api/{db}/exercises/{ex['id']}/check", {"sql": "SELECT 1 AS x"})
        if wrong["correct"]:
            failures.append(f"{tag}: a wrong query was accepted")
    if len(set(map(tuple, expected_by_db.values()))) > 1:
        a, b = list(expected_by_db.items())[0], [kv for kv in expected_by_db.items() if kv[1] != list(expected_by_db.values())[0]][0]
        diff = sorted(set(a[1]) ^ set(b[1]))[:4]
        failures.append(f"#{ex['id']} {ex['title']}: results differ between {a[0]} and {b[0]}: {diff}")

after = {db: rows(call(f"/api/{db}/run", {"sql": COUNTS})["result"]) for db in DBS}
for db in DBS:
    if before[db] != after[db]:
        failures.append(f"[{db}] data changed! before={before[db]} after={after[db]}")

print(f"{len(exercises)} exercises x {len(DBS)} databases checked")
print("\n".join(failures) if failures else "ALL PASSED")
sys.exit(1 if failures else 0)
