#!/usr/bin/env bash
# Builds the replay corpus from scenarios.json using macOS `say` and ffmpeg.
# Output: corpus/audio/<id>.wav (16 kHz mono PCM) and corpus/audio/<id>.timeline.json.
# Synthetic speech is clean and uniform: it validates pipeline behavior and timing,
# NOT real-world accuracy against WeChat/Doubao.
#
# Environment (all optional; build-dialect-corpus.sh sets them):
#   ENGINE       say (default) | edge  (Microsoft Edge online voices through the edge-tts CLI)
#   VOICE        voice for every scenario, instead of the one in scenarios.json
#   VOICE_LABEL  name written to the timeline "voice" field (defaults to the voice)
#   OUT_DIR      output directory (default corpus/audio)
#   ID_SUFFIX    appended to every scenario id, e.g. "@dongbei"
#   SCENARIOS    comma-separated scenario ids to build (default all)
set -euo pipefail

HERE="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$HERE/corpus/scenarios.json"
WORK="$HERE/corpus/.work"
OUT="${OUT_DIR:-$HERE/corpus/audio}"
ENGINE="${ENGINE:-say}"
ID_SUFFIX="${ID_SUFFIX:-}"
CACHE="$HERE/corpus/.work/edge-cache"
LEAD_MS=300
TAIL_MS=900
mkdir -p "$WORK" "$OUT" "$CACHE"

duration_ms() { ffprobe -v error -show_entries format=duration -of csv=p=0 "$1" | awk '{printf "%d", $1*1000+0.5}'; }

silence() { # <ms> <out.wav>
  ffmpeg -y -loglevel error -f lavfi -i "anullsrc=r=16000:cl=mono" -t "$(awk "BEGIN{print $1/1000}")" -c:a pcm_s16le "$2"
}

render_part() { # <voice> <text> <out.wav>
  local voice="$1" text="$2" out="$3"
  case "$ENGINE" in
    say)
      local aiff="${out%.wav}.aiff"
      # Short names such as "Eddy" resolve to the English voice, which skips Chinese characters.
      case "$voice" in
        Eddy|Flo|Shelley|Sandy|Reed|Rocko) voice="$voice (Chinese (China mainland))" ;;
      esac
      say -v "$voice" -o "$aiff" "$text"
      ffmpeg -y -loglevel error -i "$aiff" -ar 16000 -ac 1 -c:a pcm_s16le "$out"
      rm -f "$aiff"
      ;;
    edge)
      # Online synthesis is cached per voice and text, so a rebuild resumes where it stopped.
      local mp3="$CACHE/$(printf '%s|%s' "$voice" "$text" | shasum | cut -c1-20).mp3"
      [[ -s "$mp3" ]] || edge-tts --voice "$voice" --text "$text" --write-media "$mp3" 2>/dev/null
      ffmpeg -y -loglevel error -i "$mp3" -ar 16000 -ac 1 -c:a pcm_s16le "$out"
      ;;
    *) echo "Unknown ENGINE '$ENGINE'" >&2; exit 2 ;;
  esac
}

build_scenario() { # <scenario json>
  local json="$1" id voice repeat_of repeat
  id="$(jq -r .id <<<"$json")"
  voice="${VOICE:-$(jq -r .voice <<<"$json")}"
  local label="${VOICE_LABEL:-$voice}" out_id="$id$ID_SUFFIX"
  repeat_of="$(jq -r '.repeatOf // empty' <<<"$json")"
  repeat="$(jq -r '.repeat // 1' <<<"$json")"
  local parts_json
  if [[ -n "$repeat_of" ]]; then
    parts_json="$(jq -c --arg r "$repeat_of" '[.scenarios[] | select(.id==$r) | .parts[]]' "$SRC")"
  else
    parts_json="$(jq -c .parts <<<"$json")"
  fi

  local dir="$WORK/$out_id"; rm -rf "$dir"; mkdir -p "$dir"
  local list="$dir/list.txt" cursor=0 index=0 timeline="[]"
  : > "$list"
  silence "$LEAD_MS" "$dir/lead.wav"; echo "file 'lead.wav'" >> "$list"; cursor=$LEAD_MS

  for ((r = 0; r < repeat; r++)); do
    local n; n="$(jq length <<<"$parts_json")"
    for ((i = 0; i < n; i++)); do
      local text pause
      text="$(jq -r ".[$i].say" <<<"$parts_json")"
      pause="$(jq -r ".[$i].pauseMs" <<<"$parts_json")"
      local w="$dir/p$index.wav"
      render_part "$voice" "$text" "$w"
      local d; d="$(duration_ms "$w")"
      echo "file 'p$index.wav'" >> "$list"
      timeline="$(jq -c --arg t "$text" --argjson s "$cursor" --argjson e "$((cursor + d))" '. + [{text:$t,startMs:$s,endMs:$e}]' <<<"$timeline")"
      cursor=$((cursor + d))
      if [[ "$pause" -gt 0 ]]; then
        silence "$pause" "$dir/s$index.wav"; echo "file 's$index.wav'" >> "$list"; cursor=$((cursor + pause))
      fi
      index=$((index + 1))
    done
    # Soak repeats get a short natural gap between rounds.
    if ((r < repeat - 1)); then
      silence 400 "$dir/g$r.wav"; echo "file 'g$r.wav'" >> "$list"; cursor=$((cursor + 400))
    fi
  done
  silence "$TAIL_MS" "$dir/tail.wav"; echo "file 'tail.wav'" >> "$list"; cursor=$((cursor + TAIL_MS))

  ffmpeg -y -loglevel error -f concat -safe 0 -i "$list" -c:a pcm_s16le "$OUT/$out_id.wav"
  local total; total="$(duration_ms "$OUT/$out_id.wav")"
  jq -n --argjson meta "$json" --argjson parts "$timeline" --argjson total "$total" --arg id "$out_id" --arg voice "$label" \
    '{id:$id, category:$meta.category, voice:$voice, durationMs:$total, parts:$parts,
      protect:$meta.protect, mustKeep:$meta.mustKeep, forbid:$meta.forbid}' > "$OUT/$out_id.timeline.json"
  echo "built $out_id (${total} ms, $index parts)"
}

while IFS= read -r scenario; do
  build_scenario "$scenario"
done < <(jq -c --arg wanted "${SCENARIOS:-}" '.scenarios[] | select($wanted == "" or (.id as $i | ($wanted | split(",") | index($i))))' "$SRC")
