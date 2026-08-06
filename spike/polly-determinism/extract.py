#!/usr/bin/env python3
"""Compress a Stryker json report into a per-mutant verdict CSV.

The full mutation-report.json is ~2.2 MB per run, almost all of it embedded
source text. Everything the determinism analysis needs is a few columns, so
these CSVs are what gets committed.

usage: extract.py <run-dir> [<run-dir> ...] <out-dir>
"""
import csv
import json
import os
import sys

FIELDS = ["file", "id", "line", "mutator", "static", "status", "coveredBy", "killedBy", "replacement"]


def rows(run_dir):
    report = os.path.join(run_dir, "reports", "mutation-report.json")
    data = json.load(open(report))
    root = data.get("projectRoot", "")
    for file_path, info in sorted(data["files"].items()):
        rel = file_path[len(root):].lstrip("/") if root and file_path.startswith(root) else file_path
        for m in info["mutants"]:
            yield {
                "file": rel,
                "id": m["id"],
                "line": m["location"]["start"]["line"],
                "mutator": m["mutatorName"],
                "static": m.get("static", False),
                "status": m["status"],
                "coveredBy": len(m.get("coveredBy") or []),
                "killedBy": len(m.get("killedBy") or []),
                "replacement": m["replacement"].replace("\n", " ")[:160],
            }


def main(argv):
    *run_dirs, out_dir = argv
    os.makedirs(out_dir, exist_ok=True)
    for run_dir in run_dirs:
        name = os.path.basename(run_dir.rstrip("/"))
        out = os.path.join(out_dir, f"{name}.csv")
        with open(out, "w", newline="") as fh:
            w = csv.DictWriter(fh, fieldnames=FIELDS)
            w.writeheader()
            n = 0
            for row in rows(run_dir):
                w.writerow(row)
                n += 1
        print(f"{out}  ({n} mutants, {os.path.getsize(out) // 1024} KB)")


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
