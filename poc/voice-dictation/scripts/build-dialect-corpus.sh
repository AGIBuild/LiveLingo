#!/usr/bin/env bash
# Builds one copy of the replay corpus per accent variant in corpus/dialects.json.
# Output: corpus-dialect/audio/<scenario>@<variant>.wav + .timeline.json (timeline "voice" = variant id).
# Needs: edge-tts (uv tool install edge-tts), ffmpeg, jq, network access to Microsoft's online voices.
# Only the synthetic scenario text is sent; no user speech or text is involved.
# Run the harness on it with: Dictation.Harness run --corpus corpus-dialect ...
set -euo pipefail

HERE="$(cd "$(dirname "$0")/.." && pwd)"
export PATH="$HOME/.local/bin:$PATH"
export ENGINE=edge
export OUT_DIR="$HERE/corpus-dialect/audio"
export SCENARIOS="$(jq -r '[.scenarios[] | select(.category != "soak") | .id] | join(",")' "$HERE/corpus/scenarios.json")"

while IFS=$'\t' read -r id voice; do
  echo "== $id ($voice)"
  VOICE="$voice" VOICE_LABEL="$id" ID_SUFFIX="@$id" "$HERE/scripts/build-corpus.sh"
done < <(jq -r '.variants[] | [.id, .voice] | @tsv' "$HERE/corpus/dialects.json")
