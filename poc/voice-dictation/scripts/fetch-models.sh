#!/usr/bin/env bash
# Downloads the pinned POC models into $VOICE_POC_MODELS (default ~/.cache/voice-poc/models).
# Archives are deleted after extraction; every extracted file is hashed into MANIFEST.sha256.
set -euo pipefail

ROOT="${VOICE_POC_MODELS:-$HOME/.cache/voice-poc/models}"
SHERPA="https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models"
mkdir -p "$ROOT"
cd "$ROOT"

fetch_archive() {
  local name="$1"
  if [[ -d "$name" ]]; then echo "skip $name"; return; fi
  echo "fetch $name"
  curl -fL --retry 3 -C - -o "$name.tar.bz2" "$SHERPA/$name.tar.bz2"
  tar -xjf "$name.tar.bz2"
  rm -f "$name.tar.bz2"
}

fetch_file() {
  local url="$1" out="$2"
  if [[ -f "$out" ]]; then echo "skip $out"; return; fi
  echo "fetch $out"
  curl -fL --retry 3 -C - -o "$out" "$url"
}

# Live-text channel and offline second pass candidates (X-ASR, Apache-2.0).
fetch_archive sherpa-onnx-x-asr-160ms-streaming-zipformer-transducer-zh-en-punct-int8-2026-06-05
fetch_archive sherpa-onnx-x-asr-480ms-streaming-zipformer-transducer-zh-en-punct-int8-2026-06-05
fetch_archive sherpa-onnx-x-asr-960ms-streaming-zipformer-transducer-zh-en-punct-int8-2026-06-05
fetch_archive sherpa-onnx-x-asr-zipformer-transducer-zh-en-punct-int8-2026-06-03

# Quality-oriented offline second-pass candidates.
fetch_archive sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25
fetch_archive sherpa-onnx-funasr-nano-int8-2025-12-30
fetch_archive sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2025-09-09

fetch_file "$SHERPA/silero_vad.onnx" silero_vad.onnx

# Cleanup models (Apache-2.0 weights).
fetch_file "https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/resolve/main/Qwen3.5-4B-Q4_K_M.gguf" Qwen3.5-4B-Q4_K_M.gguf
if [[ "${FETCH_CONTROL_LLM:-0}" == "1" ]]; then
  fetch_file "https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/main/Qwen3-4B-Instruct-2507-Q4_K_M.gguf" Qwen3-4B-Instruct-2507-Q4_K_M.gguf
fi

find . -type f \( -name '*.onnx' -o -name '*.gguf' -o -name 'tokens.txt' \) -print0 | sort -z | xargs -0 shasum -a 256 > MANIFEST.sha256
echo "done: $(wc -l < MANIFEST.sha256) files hashed"
