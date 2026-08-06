#!/usr/bin/env python3
"""Determinism analysis over the committed per-mutant verdict CSVs.

usage: analyze.py verdicts/core-vstest-1.csv verdicts/core-vstest-2.csv ...

Prints, for the given runs: the score each run reports, the verdict pattern
across them, the pairwise disagreement counts, and the mutants whose verdict
is not identical in every run.
"""
import csv
import itertools
import sys
from collections import Counter, defaultdict

DETECTED = {"Killed", "Timeout"}
UNDETECTED = {"Survived", "NoCoverage"}
SCORED = DETECTED | UNDETECTED


def load(path):
    with open(path, newline="") as fh:
        return {(r["file"], r["id"]): r for r in csv.DictReader(fh)}


def score(run):
    c = Counter(r["status"] for r in run.values())
    detected = sum(c[s] for s in DETECTED)
    total = detected + sum(c[s] for s in UNDETECTED)
    return (100.0 * detected / total if total else float("nan")), c


def main(paths):
    if len(paths) < 2:
        raise SystemExit(__doc__)
    runs = [(p, load(p)) for p in paths]

    print("per-run summary")
    for name, run in runs:
        pct, c = score(run)
        print(f"  {name:<40} score={pct:6.2f}%  Killed={c['Killed']:<4} Timeout={c['Timeout']:<4} "
              f"Survived={c['Survived']:<4} NoCoverage={c['NoCoverage']:<4}")

    keys = [k for k in runs[0][1] if runs[0][1][k]["status"] in SCORED]

    print("\npairwise verdict disagreements")
    for (n1, r1), (n2, r2) in itertools.combinations(runs, 2):
        d = sum(1 for k in keys if r1[k]["status"] != r2[k]["status"])
        print(f"  {n1} vs {n2}: {d}")

    patterns = defaultdict(list)
    for k in keys:
        patterns[tuple(r[k]["status"] for _n, r in runs)].append(k)

    print(f"\nverdict pattern across {len(runs)} runs")
    for pat, ks in sorted(patterns.items(), key=lambda kv: -len(kv[1])):
        static = sum(1 for k in ks if runs[0][1][k]["static"] == "True")
        stable = "  <- stable" if len(set(pat)) == 1 else ""
        print(f"  {' '.join(s[:7].ljust(7) for s in pat)}  n={len(ks):<4} static={static}{stable}")

    unstable = [k for k in keys if len({r[k]['status'] for _n, r in runs}) > 1]
    print(f"\nunstable mutants: {len(unstable)} of {len(keys)} scored "
          f"({100.0 * len(unstable) / len(keys):.1f}%)")
    for k in sorted(unstable):
        m = runs[0][1][k]
        print(f"  {m['file']}:{m['line']:<5} id={m['id']:<5} static={m['static']:<5} "
              f"{m['mutator'][:30]:<30} " + " ".join(r[k]['status'][:7].ljust(7) for _n, r in runs))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
