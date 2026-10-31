#!/usr/bin/env python3
"""Score JSONL output offline. References are authored POC probes, NOT a human-reviewed test set."""
import argparse
import json
import statistics
from pathlib import Path

import numpy as np
from sacrebleu.metrics import CHRF

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("files", nargs="+")
parser.add_argument("--out")
args = parser.parse_args()
results = []
for file in args.files:
    rows = [json.loads(line) for line in Path(file).read_text().splitlines() if line.strip()]
    valid = [r for r in rows if r.get("translation")]
    first = [r for r in rows if r["repeat"] == 0]
    metric = CHRF(word_order=2)
    score = metric.corpus_score([r.get("translation", {}).get("text", "") for r in first], [[r["reference"] for r in first]])
    result = {
        "file": str(Path(file).resolve()),
        "requests": len(rows),
        "cases": len(first),
        "errors": sum(bool(r.get("error")) for r in rows),
        "firstVisibleP50": float(np.percentile([r["translation"]["firstVisibleMs"] for r in valid], 50)),
        "firstVisibleP95": float(np.percentile([r["translation"]["firstVisibleMs"] for r in valid], 95)),
        "totalP50": float(np.percentile([r["translation"]["totalMs"] for r in valid], 50)),
        "totalP95": float(np.percentile([r["translation"]["totalMs"] for r in valid], 95)),
        "constraintFailures": [{"id": r["id"], "violations": r["violations"]} for r in first if r["violations"]],
        "chrfPlusPlus": score.score,
        "signature": str(metric.get_signature()),
        "referenceCaveat": "Authored by the POC author; not independent human references or a held-out test.",
    }
    results.append(result)
    print(json.dumps(result, ensure_ascii=False, indent=2))
if args.out:
    Path(args.out).write_text(json.dumps(results, ensure_ascii=False, indent=2))
