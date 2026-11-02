#!/usr/bin/env bash
set -euo pipefail

ROOT="${TRANSLATION_POC_CACHE:-$HOME/.cache/translation-poc}"
mkdir -p "$ROOT/models" "$ROOT/runtime-b11980"

fetch() {
  local url="$1" path="$2" hash="$3"
  if [[ ! -f "$path" ]]; then
    curl --fail --location --retry 3 --continue-at - "$url" --output "$path.part"
    mv "$path.part" "$path"
  fi
  printf '%s  %s\n' "$hash" "$path" | shasum -a 256 -c -
}

fetch "https://github.com/ggml-org/llama.cpp/releases/download/b11980/llama-b11980-bin-macos-arm64.tar.gz" \
  "$ROOT/llama-b11980-bin-macos-arm64.tar.gz" \
  b7d060dd22924984dcb924fdea9858bd5bec2638217b6435fd16da55524f5524
tar -xzf "$ROOT/llama-b11980-bin-macos-arm64.tar.gz" -C "$ROOT/runtime-b11980"

case "${1:-all}" in
  1.8b|all)
    fetch "https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF/resolve/a0c709d9fac510f2c807aa3af52872340dc37a4a/Hy-MT2-1.8B-Q4_K_M.gguf" \
      "$ROOT/models/Hy-MT2-1.8B-Q4_K_M.gguf" \
      dc5f44fcf1fa496ee7ad725982c0c8c553a4de00259b53af84c4b89fb0c06699
    ;;
  7b) ;;
  *) echo "Expected 1.8b, 7b or all" >&2; exit 2 ;;
esac
case "${1:-all}" in
  7b|all)
    fetch "https://huggingface.co/tencent/Hy-MT2-7B-GGUF/resolve/ab8472660ac61fac25f1af43fac2599d52a8a775/Hy-MT2-7B-Q4_K_M.gguf" \
      "$ROOT/models/Hy-MT2-7B-Q4_K_M.gguf" \
      9f96256500f3fc1ab4d64336b58f52a949a95ad7516b0c229476eef782f9f77b
    ;;
esac
