#!/usr/bin/env python3
"""Extract and diff per-mutant verdicts from Stryker json reports.

usage:  verdicts.py <report-dir-or-json> [<report-dir-or-json> ...]

With one argument: prints a verdict summary.
With several:     prints the summary for each plus a per-mutant diff table
                  of every mutant whose status is not identical in all runs.
"""
import json
import os
import sys
from collections import Counter, OrderedDict


def find_report(arg):
    if os.path.isfile(arg):
        return arg
    for root, _dirs, files in os.walk(arg):
        if "mutation-report.json" in files:
            return os.path.join(root, "mutation-report.json")
    raise SystemExit(f"no mutation-report.json under {arg}")


def load(arg):
    path = find_report(arg)
    data = json.load(open(path))
    root = data.get("projectRoot", "")
    verdicts = OrderedDict()
    for file_path, info in sorted(data["files"].items()):
        rel = file_path[len(root):].lstrip("/") if root and file_path.startswith(root) else file_path
        for m in info["mutants"]:
            # id is only unique within a file
            key = (rel, m["id"])
            verdicts[key] = {
                "status": m["status"],
                "mutator": m["mutatorName"],
                "line": m["location"]["start"]["line"],
                "static": m.get("static", False),
                "killedBy": len(m.get("killedBy") or []),
                "coveredBy": len(m.get("coveredBy") or []),
            }
    return path, verdicts


SCORED_KILLED = {"Killed", "Timeout"}
SCORED_SURVIVED = {"Survived", "NoCoverage"}


def score(verdicts):
    c = Counter(v["status"] for v in verdicts.values())
    killed = sum(c[s] for s in SCORED_KILLED)
    survived = sum(c[s] for s in SCORED_SURVIVED)
    total = killed + survived
    return (100.0 * killed / total if total else float("nan")), c


def main(argv):
    if not argv:
        raise SystemExit(__doc__)

    runs = []
    for arg in argv:
        path, verdicts = load(arg)
        pct, counts = score(verdicts)
        runs.append((arg, verdicts))
        interesting = ["Killed", "Survived", "Timeout", "NoCoverage",
                       "CompileError", "Ignored", "Skipped", "Pending"]
        parts = " ".join(f"{s}={counts[s]}" for s in interesting if counts[s])
        print(f"{arg}\n  score={pct:.2f}%  {parts}  (report: {path})")

    if len(runs) < 2:
        return 0

    all_keys = sorted({k for _n, v in runs for k in v})
    diffs = []
    for key in all_keys:
        statuses = [v.get(key, {}).get("status", "<absent>") for _n, v in runs]
        if len(set(statuses)) > 1:
            ref = next(v[key] for _n, v in runs if key in v)
            diffs.append((key, ref, statuses))

    print(f"\n=== mutants whose verdict differs across {len(runs)} runs: {len(diffs)} ===")
    if diffs:
        width = max(len(f"{f}:{i}") for (f, i), _r, _s in diffs)
        for (f, i), ref, statuses in diffs:
            print(f"{f}:{i}".ljust(width),
                  f"line {ref['line']:>5}",
                  f"{ref['mutator'][:28]:<28}",
                  " | ".join(s[:12].ljust(12) for s in statuses))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
