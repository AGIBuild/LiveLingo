#!/usr/bin/env python3
"""Independent public OPUS-MT/CTranslate2 CPU baseline; never imports product code."""
import argparse
import json
import os
import subprocess
import time
from pathlib import Path

import ctranslate2
from huggingface_hub import snapshot_download
from transformers import MarianTokenizer

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--corpus", required=True)
parser.add_argument("--out", required=True)
parser.add_argument("--repeat", type=int, default=3)
args = parser.parse_args()
root = Path(os.environ.get("TRANSLATION_POC_CACHE", str(Path.home() / ".cache/translation-poc"))) / "nmt"
models = {
    "en": ("Helsinki-NLP/opus-mt-zh-en", "cf109095479db38d6df799875e34039d4938aaa6"),
    "zh": ("Helsinki-NLP/opus-mt-en-zh", "408d9bc410a388e1d9aef112a2daba955b945255"),
}
loaded = {}
for target, (repo, revision) in models.items():
    source = root / target / "source"
    converted = root / target / "ct2-int8"
    snapshot_download(repo, revision=revision, local_dir=source,
                      allow_patterns=["*.json", "*.spm", "pytorch_model.bin"])
    if not (converted / "model.bin").exists():
        subprocess.run(["ct2-transformers-converter", "--model", str(source), "--output_dir", str(converted),
                        "--quantization", "int8"], check=True)
    loaded[target] = (MarianTokenizer.from_pretrained(source),
                      ctranslate2.Translator(str(converted), device="cpu", compute_type="int8",
                                            inter_threads=1, intra_threads=6))

cases = json.loads(Path(args.corpus).read_text())
with open(args.out, "w") as output:
    for repeat in range(args.repeat):
        for case in cases:
            tokenizer, translator = loaded[case["targetLanguage"]]
            started = time.monotonic()
            tokens = tokenizer.convert_ids_to_tokens(tokenizer.encode(case["source"]))
            result = translator.translate_batch([tokens], beam_size=4, max_decoding_length=768)[0]
            ids = tokenizer.convert_tokens_to_ids(result.hypotheses[0])
            text = tokenizer.decode(ids, skip_special_tokens=True)
            elapsed = (time.monotonic() - started) * 1000
            violations = ["missing: " + s for s in case["keep"] if s.casefold() not in text.casefold()]
            violations += ["forbidden: " + s for s in case["forbid"] if s.casefold() in text.casefold()]
            row = {**{k: case[k] for k in ("id", "category", "source", "reference", "targetLanguage")},
                   "repeat": repeat, "violations": violations,
                   "translation": {"text": text, "firstVisibleMs": elapsed, "totalMs": elapsed,
                                   "outputTokens": len(ids)}}
            output.write(json.dumps(row, ensure_ascii=False) + "\n")
            output.flush()
            print(case["id"], repeat, round(elapsed), "ms", len(violations), "violations", flush=True)
