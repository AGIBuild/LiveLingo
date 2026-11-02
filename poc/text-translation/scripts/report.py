"""Aggregates scores/items.jsonl into results tables, paired bootstrap comparisons and a manual-review package.

Outputs under ~/VoicePoc/translation-eval/:
  report/results.md       all tables
  report/results.json     machine-readable aggregates
  review/index.html       side-by-side review sheet (source, reference, every system, metrics, checks)
  review/review.csv       same rows with empty columns for human scores
"""
import csv
import html
import json
import random
import statistics
from collections import defaultdict
from pathlib import Path

ROOT = Path.home() / "VoicePoc" / "translation-eval"
ITEMS = ROOT / "scores" / "items.jsonl"
REPORT = ROOT / "report"
REVIEW = ROOT / "review"
BOOTSTRAP = 2000
SEED = 7
QUALITY_SETS = ["wmt24-en-zh", "wmt25-en-zh", "wmt23-zh-en", "iwslt17-zh-en"]
CORE = ["wmt24-en-zh", "wmt23-zh-en"]
MAIN = "hy7b"
REFERENCE_FIRST = [MAIN, "hy7b-whole", "hy18b", "qwen35-4b", "hy7b-noprotect", "opus-mt", "hy7b-quoted", "hy7b-quoted-whole",
                   "hy7b-quoted-noctx", "hy7b-quoted-noprotect", "hy18b-quoted", "qwen35-4b-quoted", "hy7b-json"]


def baseline_of(system):
    """Ablations on a sample are compared with the main run of the same prompt contract on the same items."""
    return "hy7b-quoted" if "quoted" in system or system == "hy7b-json" else MAIN


def load() -> list[dict]:
    return [json.loads(l) for l in ITEMS.read_text(encoding="utf-8").splitlines()]


def mean(values):
    values = list(values)
    return statistics.fmean(values) if values else float("nan")


def percentile(values, p):
    values = sorted(values)
    if not values:
        return float("nan")
    k = (len(values) - 1) * p
    lo, hi = int(k), min(int(k) + 1, len(values) - 1)
    return values[lo] + (values[hi] - values[lo]) * (k - lo)


def order(systems):
    known = [s for s in REFERENCE_FIRST if s in systems]
    return known + sorted(s for s in systems if s not in REFERENCE_FIRST)


def table(headers, rows) -> str:
    lines = ["| " + " | ".join(headers) + " |", "|" + "|".join("---" for _ in headers) + "|"]
    lines += ["| " + " | ".join(str(c) for c in row) + " |" for row in rows]
    return "\n".join(lines)


def fmt(value, digits=2):
    return "-" if value != value else f"{value:.{digits}f}"


def quality_tables(rows):
    by = defaultdict(list)
    for r in rows:
        if r["set"] in QUALITY_SETS:
            by[(r["system"], r["set"])].append(r)
    systems = order({s for s, _ in by})
    out = []

    def line(label, items):
        return [label, len(items), f"{100 * mean(i['failed'] for i in items):.0f}%",
                fmt(mean(i["metricx"] for i in items)), fmt(mean(i["metricx_qe"] for i in items)),
                fmt(mean(i["comet"] for i in items), 3), fmt(mean(i["chrf"] for i in items), 1),
                fmt(mean(i["bleu"] for i in items), 1)]

    for set_name in QUALITY_SETS:
        body = []
        for s in systems:
            items = by.get((s, set_name))
            if not items:
                continue
            body.append(line(s, items))
            base = baseline_of(s)
            reference = {i["item"]: i for i in by.get((base, set_name), [])}
            if s != base and len(items) < len(reference):
                body.append(line(f"↳ {base}, same {len(items)} items", [reference[i["item"]] for i in items if i["item"] in reference]))
        out.append(f"### {set_name}\n\n" + table(
            ["system", "n", "failed", "MetricX-24 ↓", "MetricX-24 QE ↓", "COMET-22 ↑", "chrF++ ↑", "BLEU ↑"], body))
    return out


def per_domain(rows, system):
    by = defaultdict(list)
    for r in rows:
        if r["system"] == system and r["set"] in QUALITY_SETS:
            by[(r["set"], r["domain"])].append(r)
    return [[s, d, len(v), fmt(mean(i["metricx"] for i in v)), fmt(mean(i["comet"] for i in v), 3),
             fmt(mean(i["chrf"] for i in v), 1)] for (s, d), v in sorted(by.items())]


def bootstrap_pair(rows, a, b, metric, sets, seed=SEED):
    """Document-clustered paired bootstrap of mean(metric_a - metric_b) over items both systems translated."""
    left = {r["item"]: r for r in rows if r["system"] == a and r["set"] in sets}
    right = {r["item"]: r for r in rows if r["system"] == b and r["set"] in sets}
    shared = sorted(set(left) & set(right))
    if len(shared) < 10:
        return None
    docs = defaultdict(list)
    for item in shared:
        docs[left[item]["doc"]].append(left[item][metric] - right[item][metric])
    keys = sorted(docs)
    rng = random.Random(seed)
    means = []
    for _ in range(BOOTSTRAP):
        sample = [d for k in (rng.choice(keys) for _ in keys) for d in docs[k]]
        means.append(mean(sample))
    means.sort()
    diff = mean(d for v in docs.values() for d in v)
    return {"n": len(shared), "diff": diff, "lo": means[int(0.025 * BOOTSTRAP)], "hi": means[int(0.975 * BOOTSTRAP)]}


def comparison_table(rows, pairs, sets, metric="metricx", better="lower"):
    body = []
    for a, b in pairs:
        result = bootstrap_pair(rows, a, b, metric, sets)
        if result is None:
            continue
        significant = result["lo"] > 0 or result["hi"] < 0
        favored = "-"
        if significant:
            a_better = (result["diff"] < 0) if better == "lower" else (result["diff"] > 0)
            favored = a if a_better else b
        body.append([a, b, result["n"], fmt(result["diff"], 3), f"[{fmt(result['lo'], 3)}, {fmt(result['hi'], 3)}]", favored])
    return table(["A", "B", "n", f"mean({metric}_A − {metric}_B)", "95% CI", "significant winner"], body)


def check_table(rows):
    by = defaultdict(list)
    for r in rows:
        if r["set"] in QUALITY_SETS + ["probes"] and not r["system"].startswith("wmt:"):
            by[r["system"]].append(r)
    body = []
    for s in order(by):
        items = by[s]
        live = [i for i in items if not i["failed"]]
        numbers = sum(i["checks"]["numbers_total"] for i in live)
        missing = sum(i["checks"]["numbers_missing"] for i in live)
        body.append([s, len(items), sum(i["failed"] for i in items),
                     sum(i["checks"]["cjk_leak"] for i in live), sum(i["checks"]["artifact"] for i in live),
                     sum(i["checks"]["repetition"] for i in live), sum(i["checks"]["escape_leak"] for i in live),
                     sum(1 for i in live if not 0.5 <= i["checks"]["length_ratio"] <= 2.0),
                     f"{missing}/{numbers}"])
    return table(["system", "items", "failed", "CJK leak (en target)", "stray JSON artifact", "repetition",
                  "escaped \\n or \\\" leaked", "length ratio outside 0.5–2", "source numbers missing"], body)


def ifmt_table(rows):
    by = defaultdict(list)
    for r in rows:
        if r["set"] == "ifmt" and r["ref_valid"]:
            by[(r["system"], r["domain"])].append(r["ifmt_rule"])
    systems = order({s for s, _ in by})
    classes = sorted({d for _, d in by})
    body = [[s] + [f"{100 * mean(by[(s, c)]):.0f}% ({len(by[(s, c)])})" if by.get((s, c)) else "-" for c in classes] for s in systems]
    return table(["system"] + classes, body)


def probe_table(rows):
    by = defaultdict(list)
    for r in rows:
        if r["set"] == "probes":
            by[r["system"]].append(r)
    body = []
    for s in order(by):
        items = by[s]
        violated = [i for i in items if i["failed"] or i["checks"].get("probe_violations")]
        body.append([s, len(items), len(violated), "; ".join(i["item"].split(":")[1] for i in violated)])
    return table(["system", "probes", "with violations", "which"], body)


def latency_table(rows):
    by = defaultdict(list)
    for r in rows:
        if r.get("timing") and r["set"] in QUALITY_SETS + ["probes"]:
            by[r["system"]].append(r)
    body = []
    for s in order(by):
        items = by[s]
        wall_per_100 = [100 * i["timing"]["wallMs"] / max(1, len(i["src"])) for i in items]
        first = [i["timing"]["firstVisibleMs"] for i in items]
        body.append([s, len(items), fmt(percentile(first, .5), 0), fmt(percentile(first, .95), 0),
                     fmt(percentile(wall_per_100, .5), 0), fmt(percentile(wall_per_100, .95), 0),
                     fmt(mean(i["timing"]["calls"] for i in items), 1)])
    return table(["system", "items", "first visible p50 ms", "p95 ms", "wall ms per 100 src chars p50", "p95", "calls/item"], body)


def stream_table():
    def read(label):
        path = ROOT / "runs" / label
        out = []
        for f in sorted(path.glob("*.jsonl")) if path.exists() else []:
            out += [json.loads(l) for l in f.read_text(encoding="utf-8").splitlines()]
        return out
    body = []
    for lang in ("en", "zh"):
        stream, batch = read(f"hy7b-stream-{lang}"), read(f"hy7b-whole-stream-{lang}")
        if not stream or not batch:
            continue
        body.append([f"stream-{lang}", len(stream),
                     fmt(percentile([r["waitAfterInputMs"] for r in stream], .5) / 1000, 2),
                     fmt(percentile([r["waitAfterInputMs"] for r in stream], .95) / 1000, 2),
                     fmt(percentile([r["wallMs"] for r in batch], .5) / 1000, 2),
                     fmt(percentile([r["wallMs"] for r in batch], .95) / 1000, 2),
                     fmt(mean(r["calls"] for r in stream), 1), fmt(mean(r["discarded"] for r in stream), 2),
                     sum(1 for r in stream if r.get("error")), sum(1 for r in batch if r.get("error"))])
    return table(["set", "n", "units: wait after speech p50 s", "p95", "translate-after-input wait p50 s", "p95",
                  "units calls/item", "discarded/item", "units failed", "fulltext failed"], body)


def cascade_table(rows):
    small = {r["item"]: r for r in rows if r["system"] == "hy18b" and r["set"] in QUALITY_SETS}
    large = {r["item"]: r for r in rows if r["system"] == MAIN and r["set"] in QUALITY_SETS}
    shared = sorted(set(small) & set(large))
    if len(shared) < 20:
        return "Not enough shared items."
    body = []
    qs = [percentile([small[i]["metricx_qe"] for i in shared], p) for p in (0.0, .2, .4, .6, .8, 1.0)]
    for threshold in qs:
        escalate = [i for i in shared if small[i]["metricx_qe"] > threshold]
        quality = mean((large[i] if i in escalate else small[i])["metricx"] for i in shared)
        cost = mean(small[i]["timing"]["wallMs"] + (large[i]["timing"]["wallMs"] if i in escalate else 0) for i in shared)
        body.append([fmt(threshold, 2), f"{100 * len(escalate) / len(shared):.0f}%", fmt(quality, 3), fmt(cost / 1000, 1)])
    reference = [["always 1.8B", "0%", fmt(mean(small[i]["metricx"] for i in shared), 3), fmt(mean(small[i]["timing"]["wallMs"] for i in shared) / 1000, 1)],
                 ["always 7B", "100%", fmt(mean(large[i]["metricx"] for i in shared), 3), fmt(mean(large[i]["timing"]["wallMs"] for i in shared) / 1000, 1)]]
    return table(["escalate if QE(1.8B) >", "escalated", "MetricX-24 ↓", "mean wall s per item"], reference + body)


def review_package(rows):
    rng = random.Random(SEED)
    systems = order({r["system"] for r in rows if not r["system"].startswith("wmt:")})
    shown = [s for s in systems if s in (MAIN, "hy7b-whole", "hy18b", "qwen35-4b", "opus-mt", "hy7b-quoted")]
    by_item = defaultdict(dict)
    meta = {}
    for r in rows:
        by_item[r["item"]][r["system"]] = r
        meta[r["item"]] = r
    selected: dict[str, str] = {}
    for set_name in QUALITY_SETS + ["ifmt", "probes"]:
        ids = [i for i in meta if meta[i]["set"] == set_name and MAIN in by_item[i]]
        if not ids:
            continue
        domains = sorted({meta[i]["domain"] for i in ids})
        for domain in domains:
            pool = [i for i in ids if meta[i]["domain"] == domain]
            if set_name in QUALITY_SETS:
                worst = sorted(pool, key=lambda i: -by_item[i][MAIN].get("metricx", 0))[:2]
                for i in worst:
                    selected.setdefault(i, f"worst MetricX for {MAIN}")
                spread = sorted(pool, key=lambda i: -(max(r.get("metricx", 0) for s, r in by_item[i].items() if s in shown)
                                                      - min(r.get("metricx", 0) for s, r in by_item[i].items() if s in shown)))[:2]
                for i in spread:
                    selected.setdefault(i, "largest system disagreement")
            else:
                failing = [i for i in pool if by_item[i][MAIN].get("ifmt_rule", 1.0) == 0.0
                           or by_item[i][MAIN]["checks"].get("probe_violations") or by_item[i][MAIN]["failed"]]
                for i in failing[:3]:
                    selected.setdefault(i, f"rule/probe failure for {MAIN}")
            rest = [i for i in pool if i not in selected]
            rng.shuffle(rest)
            for i in rest[:3]:
                selected[i] = "random"
    REVIEW.mkdir(parents=True, exist_ok=True)
    columns = ["item", "set", "domain", "why_selected", "source", "reference"] + \
              [f"{s}" for s in shown] + [f"{s}_metrics" for s in shown] + ["human_best_system", "human_critical_errors", "human_notes"]
    with (REVIEW / "review.csv").open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(columns)
        for item, why in sorted(selected.items(), key=lambda kv: (meta[kv[0]]["set"], kv[0])):
            r = meta[item]
            writer.writerow([item, r["set"], r["domain"], why, r["src"], r["ref"]] +
                            [by_item[item][s]["hyp"] if s in by_item[item] else "" for s in shown] +
                            [metric_text(by_item[item].get(s)) for s in shown] + ["", "", ""])
    cards = []
    for item, why in sorted(selected.items(), key=lambda kv: (meta[kv[0]]["set"], kv[0])):
        r = meta[item]
        blocks = "".join(
            f"<div class='sys'><h4>{html.escape(s)} <small>{html.escape(metric_text(by_item[item].get(s)))}</small></h4>"
            f"<pre>{html.escape(by_item[item][s]['hyp'] or '(failed: ' + str(by_item[item][s]['error']) + ')')}</pre></div>"
            for s in shown if s in by_item[item])
        wmt = "".join(f"<div class='sys ext'><h4>{html.escape(s)} <small>{html.escape(metric_text(by_item[item][s]))}</small></h4>"
                      f"<pre>{html.escape(by_item[item][s]['hyp'])}</pre></div>" for s in sorted(by_item[item]) if s.startswith("wmt:"))
        cards.append(f"<section><h3>{html.escape(item)} <small>{html.escape(r['domain'])} · {html.escape(why)}</small></h3>"
                     f"<div class='sys src'><h4>source</h4><pre>{html.escape(r['src'])}</pre></div>"
                     f"<div class='sys ref'><h4>human reference</h4><pre>{html.escape(r['ref'])}</pre></div>{blocks}{wmt}</section>")
    (REVIEW / "index.html").write_text(
        "<!doctype html><meta charset='utf-8'><title>Translation review</title><style>"
        "body{font:14px system-ui;margin:24px;max-width:1200px}section{border-top:2px solid #444;margin-top:28px}"
        ".sys{display:inline-block;vertical-align:top;width:48%;margin:4px;border:1px solid #ccc;padding:4px}"
        ".src{background:#f4f8ff}.ref{background:#f2fbf2}.ext{background:#fafafa}pre{white-space:pre-wrap;margin:0}small{color:#666;font-weight:400}"
        "</style><h1>Translation review sheet</h1><p>Sampled: worst-scoring, highest-disagreement, rule failures and random items "
        "per set/domain. Metrics are automatic and are shown for orientation only; record your judgment in review.csv.</p>"
        + "".join(cards), encoding="utf-8")
    return len(selected)


def metric_text(r):
    if not r:
        return ""
    parts = [f"MetricX {r['metricx']:.1f}" if "metricx" in r else "", f"COMET {r['comet']:.3f}" if "comet" in r else "",
             f"chrF++ {r['chrf']:.0f}", f"rule {r['ifmt_rule']:.0f}" if "ifmt_rule" in r else ""]
    flags = [k for k, v in r.get("checks", {}).items() if v is True]
    return " | ".join(p for p in parts if p) + (" | flags: " + ",".join(flags) if flags else "")


def main():
    rows = load()
    REPORT.mkdir(parents=True, exist_ok=True)
    systems = {r["system"] for r in rows}
    pairs = [(MAIN, "hy7b-whole"), (MAIN, "hy18b"), (MAIN, "qwen35-4b"), (MAIN, "opus-mt"),
             (MAIN, "hy7b-quoted"), (MAIN, "hy7b-json"),
             ("hy7b-quoted", "hy7b-quoted-noctx"),
             (MAIN, "wmt:GPT-4"), (MAIN, "wmt:Claude-3.5"), (MAIN, "wmt:Gemini-1.5-Pro"),
             (MAIN, "wmt:ONLINE-B"), (MAIN, "wmt:GPT4-5shot"), (MAIN, "wmt:ONLINE-A")]
    sections = ["# Translation evaluation results (auto-generated)", "## 1. Quality per test set"] + quality_tables(rows)
    sections += [f"## 2. Per-domain breakdown: {MAIN}", table(["set", "domain", "n", "MetricX ↓", "COMET ↑", "chrF++ ↑"], per_domain(rows, MAIN))]
    sections += ["## 3. Paired bootstrap on MetricX-24 (negative diff = A better), core sets",
                 comparison_table(rows, pairs, CORE),
                 "Same on COMET-22 (positive diff = A better):", comparison_table(rows, pairs, CORE, "comet", "higher")]
    sections += ["## 4. Deterministic checks", check_table(rows), "### Authored probes (not independent)", probe_table(rows)]
    sections += ["## 5. Rule-checked format and terminology (IFMTBench, pass rate)", ifmt_table(rows)]
    sections += ["## 6. Latency (single request at a time)", latency_table(rows), "### Streaming replay at speaking speed", stream_table()]
    sections += ["## 7. QE-routed cascade (simulated offline)", cascade_table(rows)]
    count = review_package(rows)
    sections += [f"## 8. Manual review package\n\n{count} sampled items in `review/index.html` and `review/review.csv`."]
    (REPORT / "results.md").write_text("\n\n".join(sections) + "\n", encoding="utf-8")
    print("\n\n".join(sections))


if __name__ == "__main__":
    main()
