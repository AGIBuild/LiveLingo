#!/usr/bin/env python3
"""OPUS-MT/CTranslate2 CPU baseline over corpus sets, written in the same record format as the C# runner.

Conventional NMT pipeline: sentence split, translate each sentence independently, rejoin.
Never imports product code.
"""
import argparse
import json
import os
import re
import time
from pathlib import Path

import ctranslate2
from transformers import MarianTokenizer

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--sets", nargs="+", required=True)
parser.add_argument("--label", default="opus-mt")
args = parser.parse_args()

cache = Path(os.environ.get("TRANSLATION_POC_CACHE", str(Path.home() / ".cache/translation-poc"))) / "nmt"
root = Path.home() / "VoicePoc" / "translation-eval"
loaded = {
    target: (MarianTokenizer.from_pretrained(cache / target / "source"),
             ctranslate2.Translator(str(cache / target / "ct2-int8"), device="cpu", compute_type="int8",
                                    inter_threads=1, intra_threads=6))
    for target in ("en", "zh")
}
SENTENCE_END = re.compile(r"(?<=[.!?。！？])\s+|(?<=[。！？])")


def split_lines(text: str) -> list[tuple[str, str]]:
    """Returns (break-before, sentence) pairs; line breaks are kept, same-line breaks are empty."""
    parts, pending_break = [], ""
    for raw in re.split(r"(\n+)", text):
        if raw.startswith("\n"):
            pending_break = raw
            continue
        for sentence in SENTENCE_END.split(raw):
            if sentence.strip():
                parts.append((pending_break, sentence.strip()))
                pending_break = ""
    return parts


for set_name in args.sets:
    out_path = root / "runs" / args.label / f"{set_name}.jsonl"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    done = {json.loads(l)["id"] for l in out_path.read_text(encoding="utf-8").splitlines()} if out_path.exists() else set()
    items = [json.loads(l) for l in (root / "corpus" / f"{set_name}.jsonl").read_text(encoding="utf-8").splitlines()]
    with out_path.open("a", encoding="utf-8") as output:
        for item in items:
            if item["id"] in done:
                continue
            target = "zh" if item["direction"].endswith("-zh") else "en"
            tokenizer, translator = loaded[target]
            sentences = split_lines(item["src"])
            started = time.monotonic()
            translated, unit_records = [], []
            for lead, sentence in sentences:
                s = time.monotonic()
                tokens = tokenizer.convert_ids_to_tokens(tokenizer.encode(sentence))
                result = translator.translate_batch([tokens], beam_size=4, max_decoding_length=512)[0]
                text = tokenizer.decode(tokenizer.convert_tokens_to_ids(result.hypotheses[0]), skip_special_tokens=True)
                translated.append((lead, text))
                unit_records.append({"source": sentence, "target": text, "totalMs": (time.monotonic() - s) * 1000})
            same_line = " " if target == "en" else ""
            hyp = ""
            for index, (lead, text) in enumerate(translated):
                hyp += (lead or same_line if index else "") + text
            wall = (time.monotonic() - started) * 1000
            record = {"id": item["id"], "strategy": "units", "hyp": hyp, "calls": len(sentences),
                      "callMs": wall, "firstVisibleMs": unit_records[0]["totalMs"] if unit_records else 0,
                      "wallMs": wall, "outputTokens": 0, "discarded": 0, "waitAfterInputMs": wall,
                      "firstConfirmedMs": 0, "units": unit_records}
            output.write(json.dumps(record, ensure_ascii=False) + "\n")
            output.flush()
            print(set_name, item["id"], round(wall), "ms", flush=True)
