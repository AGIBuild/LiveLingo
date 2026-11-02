"""Scores translation runs against the corpus.

Per (system, item) metrics, cached by content hash so reruns only score new outputs:
  chrF++, BLEU, COMET-22 (reference), MetricX-24 hybrid XL (reference and QE),
  deterministic checks (CJK leakage, numbers, artifacts, repetition, length ratio, probes, IFMTBench rules).
Systems are LLM runs under runs/<label>/<set>.jsonl, the OPUS-MT run under runs/opus-mt/, and the public
WMT systems embedded in the corpus (labelled wmt:<name>) as external reference points.

Failed items (error or empty output) are kept in the denominator with penalty scores:
chrF++/BLEU 0, COMET 0, MetricX 25 (its maximum).
"""
import argparse
import hashlib
import importlib.util
import json
import re
import sys
import unicodedata
from pathlib import Path

import sacrebleu

ROOT = Path.home() / "VoicePoc" / "translation-eval"
CORPUS = ROOT / "corpus"
RUNS = ROOT / "runs"
SCORES = ROOT / "scores"
IFMT_EVAL = ROOT / "corpus-src" / "ifmt"
METRICX_SRC = Path.home() / ".cache/translation-poc/metricx-src"
METRICX_MODEL = "google/metricx-24-hybrid-xl-v2p6-bfloat16"
COMET_MODEL = "Unbabel/wmt22-comet-da"
COMET_PENALTY, METRICX_PENALTY = 0.0, 25.0
SETS = ["wmt24-en-zh", "wmt25-en-zh", "wmt23-zh-en", "iwslt17-zh-en", "ifmt", "probes"]

CJK = re.compile(r"[\u4e00-\u9fff]")
NUMBER = re.compile(r"\d+(?:[.,]\d+)*")


def digest(*parts: str) -> str:
    return hashlib.sha1("\x1f".join(parts).encode("utf-8")).hexdigest()


def load_corpus() -> dict[str, dict]:
    items = {}
    for name in SETS:
        for line in (CORPUS / f"{name}.jsonl").read_text(encoding="utf-8").splitlines():
            item = json.loads(line)
            items[item["id"]] = item
    return items


def load_outputs(corpus: dict[str, dict]) -> list[dict]:
    """Returns one record per (system, item) including failures."""
    outputs = []
    for system_dir in sorted(p for p in RUNS.iterdir() if p.is_dir()):
        label = system_dir.name
        if "-stream-" in label:
            continue
        for set_name in SETS:
            path = system_dir / f"{set_name}.jsonl"
            if not path.exists():
                continue
            done = {}
            for line in path.read_text(encoding="utf-8").splitlines():
                record = json.loads(line)
                done[record["id"]] = record
            for item_id, record in done.items():
                outputs.append({"system": label, "item": item_id, "hyp": record.get("hyp", ""),
                                "error": record.get("error"), "run": record})
    seen = {(o["system"], o["item"]) for o in outputs}
    for item in corpus.values():
        for name, hyp in item.get("systems", {}).items():
            outputs.append({"system": f"wmt:{name}", "item": item["id"], "hyp": hyp, "error": None, "run": None})
    return outputs


def deterministic(item: dict, hyp: str, target: str) -> dict:
    src = item["src"]
    checks = {}
    checks["cjk_leak"] = bool(CJK.search(hyp)) if target == "en" else False
    src_numbers = {n for n in NUMBER.findall(src)}
    checks["numbers_missing"] = sum(1 for n in src_numbers if n not in hyp) if target and src_numbers else 0
    checks["numbers_total"] = len(src_numbers)
    checks["artifact"] = bool(re.search(r"\{\s*\"text\"|[」”\"]\}\s*$", hyp)) and not re.search(r"\}\s*$", src)
    words = re.findall(r"\w+", hyp.lower()) if target == "en" else list(hyp)
    n = 6 if target == "en" else 12
    grams: dict[tuple, int] = {}
    for i in range(max(0, len(words) - n + 1)):
        gram = tuple(words[i:i + n])
        grams[gram] = grams.get(gram, 0) + 1
    checks["repetition"] = any(v >= 3 for v in grams.values())
    # JSON string escapes copied from a quoted source corrupt line breaks and quotes in the visible output.
    checks["escape_leak"] = any(e in hyp and e not in src for e in ("\\n", '\\"'))
    ref = item["ref"]
    checks["length_ratio"] = len(hyp) / max(1, len(ref))
    if item["set"] == "probes":
        checks["probe_violations"] = [f"missing:{k}" for k in item.get("keep", []) if k.lower() not in hyp.lower()] + \
                                     [f"forbidden:{k}" for k in item.get("forbid", []) if k.lower() in hyp.lower()]
    return checks


def ifmt_rule(item: dict, hyp: str) -> float:
    sys.path.insert(0, str(IFMT_EVAL))
    from eval.rule_validators import validate_glossary, validate_layout, validate_structured, validate_code_assets
    row = item["ifmt"]
    kind = item["domain"]
    meta = row.get("meta_data", {})
    if not hyp:
        return 0.0
    if kind == "glossary":
        return 1.0 if validate_glossary(hyp, row.get("term_dict", ""), row["output"])["valid"] else 0.0
    if kind == "structured":
        return 1.0 if validate_structured(row["origin_text"], hyp, row.get("data_format", ""))["valid"] else 0.0
    if kind == "layout":
        return 1.0 if validate_layout(hyp, meta, row["origin_text"])["valid"] else 0.0
    return 1.0 if validate_code_assets(hyp, meta.get("extracted_assets", []))["valid"] else 0.0


def script_ok(text: str, lang: str) -> bool:
    """True when the text is plausibly written in zh or en; IFMTBench contains rows whose labelled language is wrong."""
    letters = [c for c in text if c.isalpha()]
    if not letters:
        return True
    cjk = sum(1 for c in letters if "\u4e00" <= c <= "\u9fff")
    ascii_letters = sum(1 for c in letters if c.isascii())
    return cjk / len(letters) >= 0.3 if lang == "zh" else ascii_letters / len(letters) >= 0.8


def garbled(text: str) -> bool:
    """IFMTBench contains rows with private-use code points scrambled into terms and sentences."""
    return any(unicodedata.category(c) == "Co" for c in text)


def reference_valid(item: dict) -> bool:
    """IFMTBench rows are only usable when their own reference satisfies the rule, the languages match the labels
    and no text in the row is garbled."""
    source_lang, target_lang = item["direction"].split("-")
    row_text = item["src"] + item["ref"] + json.dumps(item.get("terms", []), ensure_ascii=False)
    return (not garbled(row_text) and script_ok(item["src"], source_lang) and script_ok(item["ref"], target_lang)
            and ifmt_rule(item, item["ref"]) == 1.0)


def overlap_metrics(hyp: str, ref: str, target: str) -> dict:
    tokenize = "zh" if target == "zh" else "13a"
    return {
        "chrf": sacrebleu.sentence_chrf(hyp, [ref], word_order=2).score,
        "bleu": sacrebleu.sentence_bleu(hyp, [ref], tokenize=tokenize).score,
    }


def load_cache(name: str) -> dict[str, float]:
    path = SCORES / f"cache-{name}.jsonl"
    cache = {}
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            row = json.loads(line)
            cache[row["key"]] = row["score"]
    return cache


def append_cache(name: str, rows: list[tuple[str, float]]) -> None:
    with (SCORES / f"cache-{name}.jsonl").open("a", encoding="utf-8") as handle:
        for key, score in rows:
            handle.write(json.dumps({"key": key, "score": score}) + "\n")


def comet_scores(pending: list[dict]) -> list[float]:
    from comet import download_model, load_from_checkpoint
    model = load_from_checkpoint(download_model(COMET_MODEL))
    data = [{"src": p["src"], "mt": p["hyp"], "ref": p["ref"]} for p in pending]
    return list(model.predict(data, batch_size=16, accelerator="mps", gpus=1, progress_bar=True).scores)


def metricx_model():
    import torch
    import transformers
    source = (METRICX_SRC / "metricx24" / "models.py").read_text(encoding="utf-8")
    # Upstream moves the decoder start token to CUDA only; Apple GPUs need it on the input's device.
    source = source.replace(
        "    if torch.cuda.is_available():\n      decoder_input_ids = decoder_input_ids.to(torch.device(\"cuda\"))",
        "    decoder_input_ids = decoder_input_ids.to(input_ids.device)")
    namespace: dict = {"__name__": "metricx_models"}
    exec(compile(source, "metricx24/models.py", "exec"), namespace)
    tokenizer = transformers.AutoTokenizer.from_pretrained("google/mt5-xl")
    model = namespace["MT5ForRegression"].from_pretrained(METRICX_MODEL, torch_dtype=torch.bfloat16)
    return tokenizer, model.to("mps").eval()


def metricx_scores(pending: list[dict], qe: bool) -> list[float]:
    import torch
    tokenizer, model = metricx_model()
    texts = [("source: " + p["src"] + " candidate: " + p["hyp"] + ("" if qe else " reference: " + p["ref"])) for p in pending]
    order = sorted(range(len(texts)), key=lambda i: len(texts[i]))
    scores = [0.0] * len(texts)
    batch_size = 8
    for start in range(0, len(order), batch_size):
        indexes = order[start:start + batch_size]
        encoded = [tokenizer(texts[i], max_length=1536, truncation=True)["input_ids"][:-1] for i in indexes]
        width = max(len(e) for e in encoded)
        ids = torch.tensor([e + [tokenizer.pad_token_id] * (width - len(e)) for e in encoded])
        mask = torch.tensor([[1] * len(e) + [0] * (width - len(e)) for e in encoded])
        with torch.no_grad():
            out = model(input_ids=ids.to("mps"), attention_mask=mask.to("mps"))
        for i, value in zip(indexes, out.predictions.float().cpu().tolist()):
            scores[i] = value
        print(f"metricx{'-qe' if qe else ''} {min(start + batch_size, len(order))}/{len(order)}", flush=True)
    return scores


def learned(name: str, rows: list[dict], compute) -> dict[str, float]:
    cache = load_cache(name)
    pending, keys = [], []
    for row in rows:
        key = digest(name, row["src"], row["hyp"], row["ref"])
        if key not in cache and key not in keys:
            pending.append(row)
            keys.append(key)
    if pending:
        print(f"{name}: scoring {len(pending)} new outputs", flush=True)
        scores = compute(pending)
        append_cache(name, list(zip(keys, scores)))
        cache.update(zip(keys, scores))
    return cache


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--skip-learned", action="store_true", help="only cheap metrics and checks")
    args = parser.parse_args()
    SCORES.mkdir(parents=True, exist_ok=True)
    corpus = load_corpus()
    outputs = load_outputs(corpus)
    rows = []
    for o in outputs:
        item = corpus[o["item"]]
        target = "zh" if item["direction"].endswith("-zh") else "en"
        failed = bool(o["error"]) or not o["hyp"].strip()
        row = {"system": o["system"], "item": item["id"], "set": item["set"], "domain": item["domain"],
               "direction": item["direction"], "doc": item["doc"], "src": item["src"], "ref": item["ref"],
               "hyp": o["hyp"], "error": o["error"], "failed": failed, "target": target}
        row.update({"chrf": 0.0, "bleu": 0.0} if failed else overlap_metrics(o["hyp"], item["ref"], target))
        row["checks"] = deterministic(item, o["hyp"], target) if not failed else {}
        if item["set"] == "ifmt":
            leaked = bool(row["checks"].get("escape_leak"))
            row["ifmt_rule"] = 0.0 if leaked else ifmt_rule(item, o["hyp"])
            row["ref_valid"] = reference_valid(item)
        if o["run"]:
            run = o["run"]
            row["timing"] = {k: run.get(k) for k in ("calls", "callMs", "firstVisibleMs", "wallMs", "outputTokens", "discarded")}
        rows.append(row)
    if not args.skip_learned:
        live = [r for r in rows if not r["failed"] and r["set"] != "ifmt"]
        comet = learned("comet22", live, comet_scores)
        mx_ref = learned("metricx24-ref", live, lambda p: metricx_scores(p, qe=False))
        mx_qe = learned("metricx24-qe", live, lambda p: metricx_scores(p, qe=True))
        for r in rows:
            if r["set"] == "ifmt":
                continue
            if r["failed"]:
                r.update({"comet": COMET_PENALTY, "metricx": METRICX_PENALTY, "metricx_qe": METRICX_PENALTY})
                continue
            r["comet"] = comet[digest("comet22", r["src"], r["hyp"], r["ref"])]
            r["metricx"] = mx_ref[digest("metricx24-ref", r["src"], r["hyp"], r["ref"])]
            r["metricx_qe"] = mx_qe[digest("metricx24-qe", r["src"], r["hyp"], r["ref"])]
    with (SCORES / "items.jsonl").open("w", encoding="utf-8") as handle:
        for r in rows:
            handle.write(json.dumps(r, ensure_ascii=False) + "\n")
    print(f"wrote {len(rows)} scored outputs")


if __name__ == "__main__":
    main()
