#!/usr/bin/env python3
"""Sample an explicitly selected local server PID; RSS is not total Metal/unified-memory usage."""
import argparse
import json
import subprocess
import time
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--pid", type=int, required=True)
parser.add_argument("--seconds", type=int, default=120)
parser.add_argument("--out", required=True)
args = parser.parse_args()
start = time.monotonic()
samples = []
while time.monotonic() - start < args.seconds:
    probe = subprocess.run(["ps", "-o", "rss=,pcpu=", "-p", str(args.pid)], capture_output=True, text=True)
    if probe.returncode:
        raise SystemExit(f"Process {args.pid} exited before resource sampling finished.")
    rss, cpu = probe.stdout.split()
    samples.append({"elapsedMs": (time.monotonic() - start) * 1000, "rssKiB": int(rss), "cpuPercent": float(cpu)})
    time.sleep(0.5)
result = {
    "pid": args.pid, "sampledPeakRssMiB": max(s["rssKiB"] for s in samples) / 1024,
    "samples": samples,
    "caveat": "Sampled process RSS only; shared/Metal allocations and sub-500 ms peaks are not fully represented."
}
Path(args.out).write_text(json.dumps(result, indent=2))
print("sampledPeakRssMiB", result["sampledPeakRssMiB"])
