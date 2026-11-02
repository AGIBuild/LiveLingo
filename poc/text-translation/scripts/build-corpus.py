"""Builds the translation evaluation corpus under ~/VoicePoc/translation-eval/corpus.

Independent human-reference sets (not authored by us):
  wmt24-en-zh   WMT24 general test (sacreBLEU), domains literary/news/social/speech
  wmt25-en-zh   WMT25 general test, human reference refA, newest and least likely to be in training data
  wmt23-zh-en   WMT23 general test, domains news/ecommerce/social/conversation
  iwslt17-zh-en TED talks, spoken register
  ifmt          Tencent IFMTBench (CC-BY-4.0), rule-checkable glossary/code/format items
Authored by us, reported separately: probes (cases.json).

Items are consecutive-paragraph chunks so passage-level behaviour is measured while
metrics stay inside their input windows. Sampling is stratified and seeded.
"""
import argparse
import json
import random
import subprocess
import urllib.request
from pathlib import Path

ROOT = Path.home() / "VoicePoc" / "translation-eval"
SRC = ROOT / "corpus-src"
OUT = ROOT / "corpus"
SACREBLEU = Path.home() / ".cache/translation-poc/eval-env/bin/sacrebleu"
SEED = 20251029
MAX_CHARS = 900
MAX_SEGMENTS = 8
MAX_SEGMENT_CHARS = 1800

WMT25_URL = "https://github.com/wmt-conference/wmt25-general-mt/raw/refs/heads/main/data/wmt25-genmt.jsonl"
IFMT_URL = "https://huggingface.co/datasets/tencent/IFMTBench/resolve/main/data/"


def download(url: str, path: Path) -> Path:
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        urllib.request.urlretrieve(url, path)
    return path


def sacrebleu_rows(test_set: str, pair: str, fields: list[str]) -> list[dict]:
    path = SRC / f"{test_set}-{pair}.tsv"
    if not path.exists():
        with path.open("w", encoding="utf-8") as out:
            subprocess.run([str(SACREBLEU), "-t", test_set, "-l", pair, "--echo", *fields],
                           stdout=out, check=True)
    rows = []
    for line in path.read_text(encoding="utf-8").split("\n")[:-1]:
        cells = line.split("\t")
        if len(cells) != len(fields):
            raise ValueError(f"{path}: expected {len(fields)} columns, got {len(cells)}")
        rows.append(dict(zip(fields, cells)))
    return rows


def chunk(segments: list[dict], joiner_src: str, joiner_ref: str) -> list[dict]:
    """Groups consecutive segments of one document into chunks bounded by size."""
    chunks, current = [], []
    for seg in segments:
        if len(seg["src"]) > MAX_SEGMENT_CHARS:
            if current:
                chunks.append(current)
                current = []
            continue
        size = sum(len(s["src"]) for s in current) + len(seg["src"])
        if current and (size > MAX_CHARS or len(current) >= MAX_SEGMENTS):
            chunks.append(current)
            current = []
        current.append(seg)
    if current:
        chunks.append(current)
    result = []
    for index, group in enumerate(chunks):
        systems = {name: joiner_ref.join(s["systems"][name] for s in group) for name in group[0]["systems"]}
        result.append({
            "src": joiner_src.join(s["src"] for s in group),
            "ref": joiner_ref.join(s["ref"] for s in group),
            "systems": systems,
            "segments": len(group),
            "index": index,
        })
    return result


def sample(items: list[dict], per_domain: int, rng: random.Random) -> list[dict]:
    by_domain: dict[str, list[dict]] = {}
    for item in items:
        by_domain.setdefault(item["domain"], []).append(item)
    picked = []
    for domain in sorted(by_domain):
        pool = sorted(by_domain[domain], key=lambda i: i["id"])
        rng.shuffle(pool)
        picked.extend(sorted(pool[:per_domain], key=lambda i: i["id"]))
    return picked


def from_sacrebleu(set_name: str, test_set: str, pair: str, direction: str, system_names: list[str],
                   domain_field: str, per_domain: int, joiner_src: str, joiner_ref: str, rng: random.Random,
                   ref_field: str = "ref:refA") -> list[dict]:
    fields = ["src", ref_field, "docid", domain_field, *system_names]
    docs: dict[str, list[dict]] = {}
    domains: dict[str, str] = {}
    for row in sacrebleu_rows(test_set, pair, fields):
        docs.setdefault(row["docid"], []).append({
            "src": row["src"], "ref": row[ref_field],
            "systems": {n: row[n] for n in system_names}})
        domains[row["docid"]] = row[domain_field]
    items = []
    for doc_id, segments in docs.items():
        for c in chunk(segments, joiner_src, joiner_ref):
            items.append({"id": f"{set_name}:{doc_id}:{c['index']}", "set": set_name, "direction": direction,
                          "domain": domains[doc_id], "doc": doc_id, "src": c["src"], "ref": c["ref"],
                          "systems": c["systems"]})
    return sample(items, per_domain, rng)


def from_wmt25(per_domain: int, rng: random.Random) -> list[dict]:
    path = download(WMT25_URL, SRC / "wmt25-genmt.jsonl")
    items, skipped = [], 0
    for line in path.read_text(encoding="utf-8").splitlines():
        row = json.loads(line)
        if row["src_lang"] != "en" or row["tgt_lang"] != "zh_CN":
            continue
        src_lines = [l for l in row["src_text"].split("\n") if l.strip()]
        ref_lines = [l for l in row["refs"]["refA"]["ref"].split("\n") if l.strip()]
        if len(src_lines) != len(ref_lines):
            skipped += 1
            continue
        segments = [{"src": s, "ref": r, "systems": {}} for s, r in zip(src_lines, ref_lines)]
        for c in chunk(segments, "\n", "\n"):
            items.append({"id": f"wmt25-en-zh:{row['doc_id'].split('_#_')[-1]}:{c['index']}", "set": "wmt25-en-zh",
                          "direction": "en-zh", "domain": row["domain"], "doc": row["doc_id"],
                          "src": c["src"], "ref": c["ref"], "systems": {}})
    print(f"wmt25-en-zh: {skipped} documents skipped because paragraph counts differ")
    return sample(items, per_domain, rng)


def from_ifmt(per_class: int, rng: random.Random) -> list[dict]:
    wanted = {
        "机器翻译-术语表约束翻译": "glossary",
        "机器翻译-代码标签保留翻译": "code-tag",
        "机器翻译-内联代码保留翻译": "inline-code",
        "机器翻译-结构化数据翻译": "structured",
        "机器翻译-布局保留翻译": "layout",
    }
    directions = {("英文", "中文"): "en-zh", ("英语", "中文"): "en-zh", ("中文", "英文"): "zh-en", ("中文", "英语"): "zh-en"}
    pools: dict[tuple[str, str], list[dict]] = {}
    for name in ("test_single_constraint.jsonl",):
        path = download(IFMT_URL + name, SRC / f"ifmt-{name}")
        for line in path.read_text(encoding="utf-8").splitlines():
            row = json.loads(line)
            kind = wanted.get(row["class"][0]) if len(row["class"]) == 1 else None
            direction = directions.get((row.get("origin_language"), row.get("target_language")))
            if kind is None or direction is None or len(row["origin_text"]) > MAX_SEGMENT_CHARS:
                continue
            terms = []
            if kind == "glossary":
                if "term_dict" not in row:
                    continue
                term_dict = json.loads(row["term_dict"]) if isinstance(row["term_dict"], str) else row["term_dict"]
                if any(not isinstance(v, str) and len(v) != 1 for v in term_dict.values()):
                    continue
                terms = [{"source": k, "target": v if isinstance(v, str) else v[0]} for k, v in term_dict.items()]
            pools.setdefault((kind, direction), []).append({
                "id": f"ifmt:{kind}:{row['md5'][:10]}", "set": "ifmt", "direction": direction, "domain": kind,
                "doc": row["md5"], "src": row["origin_text"], "ref": row["output"], "terms": terms, "ifmt": row})
    items = []
    for key in sorted(pools):
        pool = sorted(pools[key], key=lambda i: i["id"])
        rng.shuffle(pool)
        items.extend(sorted(pool[:per_class], key=lambda i: i["id"]))
    return items


def from_probes() -> list[dict]:
    cases = json.loads((Path(__file__).parent.parent / "cases.json").read_text(encoding="utf-8"))
    return [{"id": f"probe:{c['id']}", "set": "probes", "direction": "en-zh" if c["targetLanguage"] == "zh" else "zh-en",
             "domain": c["category"], "doc": c["id"], "src": c["source"], "ref": c["reference"],
             "terms": [{"source": t["source"], "target": t["target"]} for t in c.get("terms", [])],
             "keep": c.get("keep", []), "forbid": c.get("forbid", [])} for c in cases]


def write(name: str, items: list[dict]) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    with (OUT / f"{name}.jsonl").open("w", encoding="utf-8") as handle:
        for item in items:
            handle.write(json.dumps(item, ensure_ascii=False) + "\n")
    chars = sum(len(i["src"]) for i in items)
    print(f"{name}: {len(items)} items, {chars} source chars")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--per-domain", type=int, default=15)
    parser.add_argument("--per-domain-zh-en", type=int, default=30)
    parser.add_argument("--per-ifmt-class", type=int, default=20)
    args = parser.parse_args()
    rng = random.Random(SEED)
    systems_24 = ["GPT-4", "Claude-3.5", "Gemini-1.5-Pro", "ONLINE-B", "Llama3-70B"]
    systems_23 = ["GPT4-5shot", "ONLINE-A", "ONLINE-B", "ONLINE-W"]
    wmt24 = from_sacrebleu("wmt24-en-zh", "wmt24", "en-zh", "en-zh", systems_24, "domain", args.per_domain, "\n", "\n", rng)
    wmt25 = from_wmt25(args.per_domain, rng)
    wmt23 = from_sacrebleu("wmt23-zh-en", "wmt23", "zh-en", "zh-en", systems_23, "domain", args.per_domain_zh_en, "\n", "\n", rng)
    iwslt = from_sacrebleu("iwslt17-zh-en", "iwslt17", "zh-en", "zh-en", [], "genre", args.per_domain_zh_en, "", " ", rng,
                           ref_field="ref")
    write("wmt24-en-zh", wmt24)
    write("wmt25-en-zh", wmt25)
    write("wmt23-zh-en", wmt23)
    write("iwslt17-zh-en", iwslt)
    write("ifmt", from_ifmt(args.per_ifmt_class, rng))
    write("probes", from_probes())
    # Spoken-style items replayed at speaking speed to measure streaming behaviour.
    write("stream-en", [i for i in wmt25 if i["domain"] == "speech"][:10])
    write("stream-zh", sorted([i for i in iwslt if len(i["src"]) >= 150], key=lambda i: i["id"])[:10])


if __name__ == "__main__":
    main()
