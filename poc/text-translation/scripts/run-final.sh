#!/usr/bin/env bash
# Final-design runs: plain-text prompt contract and whitespace-preserving units. Sequential so latency numbers are
# not disturbed by other model runs. Runs are resumable.
# The earlier exploration (JSON/quoted contracts, context and literal-protection ablations) lives in runs/*-quoted,
# runs/hy7b-json, runs/hy7b-noctx and was produced by the code state described in REPORT.md.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
models="$HOME/.cache/translation-poc/models"
qwen="$HOME/.cache/voice-poc/models/Qwen3.5-4B-Q4_K_M.gguf"
all="wmt24-en-zh wmt25-en-zh wmt23-zh-en iwslt17-zh-en ifmt probes"

"$here/run-llm.sh" hy7b            "$models/Hy-MT2-7B-Q4_K_M.gguf"   units    2 on  "$all"
"$here/run-llm.sh" hy7b-whole      "$models/Hy-MT2-7B-Q4_K_M.gguf"   fulltext 2 on  "$all"
"$here/run-llm.sh" hy18b           "$models/Hy-MT2-1.8B-Q4_K_M.gguf" units    2 on  "$all"
LIMIT=60 "$here/run-llm.sh" qwen35-4b "$qwen"                        units    2 on  "wmt24-en-zh wmt23-zh-en ifmt probes"
"$here/run-llm.sh" hy7b-noprotect  "$models/Hy-MT2-7B-Q4_K_M.gguf"   units    2 off "ifmt probes"
"$here/run-llm.sh" hy7b-stream-en  "$models/Hy-MT2-7B-Q4_K_M.gguf"   stream   2 on  "stream-en" 15
"$here/run-llm.sh" hy7b-stream-zh  "$models/Hy-MT2-7B-Q4_K_M.gguf"   stream   2 on  "stream-zh" 5
"$here/run-llm.sh" hy7b-whole-stream-en "$models/Hy-MT2-7B-Q4_K_M.gguf" fulltext 2 on "stream-en"
"$here/run-llm.sh" hy7b-whole-stream-zh "$models/Hy-MT2-7B-Q4_K_M.gguf" fulltext 2 on "stream-zh"
echo ALL-RUNS-DONE
