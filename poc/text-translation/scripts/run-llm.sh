#!/usr/bin/env bash
# Runs one system configuration over corpus sets with a dedicated local llama-server.
# Usage: run-llm.sh LABEL GGUF STRATEGY CONTEXT PROTECT "SET SET ..." [RATE_CHARS_PER_SEC]
# Output: ~/VoicePoc/translation-eval/runs/LABEL/SET.jsonl (resumable)
set -euo pipefail
label=$1 model=$2 strategy=$3 context=$4 protect=$5 sets=$6 rate=${7:-5}
root="$HOME/.cache/translation-poc"
here="$(cd "$(dirname "$0")/.." && pwd)"
port=8197
"$root/runtime-b11980/llama-b11980/llama-server" --model "$model" --host 127.0.0.1 --port $port \
  --ctx-size 8192 --parallel 1 --n-gpu-layers 99 --threads 6 --reasoning-budget 0 >"/tmp/llama-$label.log" 2>&1 &
server=$!
trap 'kill $server 2>/dev/null || true' EXIT
for _ in $(seq 1 120); do
  curl -sf "http://127.0.0.1:$port/health" >/dev/null && break
  sleep 1
done
curl -sf "http://127.0.0.1:$port/health" >/dev/null
# Warm the model so the first measured item does not pay load time.
curl -sf "http://127.0.0.1:$port/v1/chat/completions" -d '{"messages":[{"role":"user","content":"hi"}],"max_tokens":4}' >/dev/null
for set in $sets; do
  dotnet "$here/bin/Release/net10.0/TranslationPoc.dll" corpus --endpoint "http://127.0.0.1:$port/" \
    --corpus "$HOME/VoicePoc/translation-eval/corpus/$set.jsonl" \
    --out "$HOME/VoicePoc/translation-eval/runs/$label/$set.jsonl" \
    --strategy "$strategy" --context "$context" --protect "$protect" --rate "$rate" ${LIMIT:+--limit $LIMIT}
done
