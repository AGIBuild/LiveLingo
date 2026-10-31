# Independent local translation POC

This project does **not** reference, import, or adapt the product translation implementation. It evaluates a new pipeline using official standalone runtimes and public models. No product defaults are changed.

## Design under test

```text
source revision / composition event
    -> complete semantic units + unfinished tail
    -> one worker, preserved completed prefix, invalidated dependent suffix
    -> matched glossary + at most two preceding source/translation pairs
    -> protected literal tokens
    -> translation-only model with typed JSON output
    -> validate schema + restore literals exactly
    -> apply only if the unit still belongs to the current source
```

The CLI currently implements completed-unit translation and flush-on-confirm. A timed provisional-tail preview, manual translation editing and an interactive UI are not implemented. Streaming timings refer to arrival of the first translated payload character, **not an actual UI paint**.

### Why these boundaries

- The source is sent verbatim after the instructions with plain-text output. Wrapping it as a JSON string (the "quoted" contract) or requiring a JSON reply (the "json" contract) both lost to the plain contract in the corpus evaluation: the model copied the JSON escapes (`\n`, `\"`) into its output, and JSON replies ran away when a source ended with a quote. The only instruction-injection defence is the prompt wording; it is covered by an authored probe, not guaranteed.
- Code fences, inline code, URLs and placeholders are replaced with collision-free tokens and restored exactly once. Missing or duplicated tokens fail explicitly.
- Model output is not polished by another model. There is one selected translation backend; no hidden cloud fallback or retry chain.
- The worker preserves completed source units on append, invalidates translations whose source context changed, ignores obsolete in-flight results, and suppresses requests during IME composition.

## Reproduce

Models and dependencies are outside the repository. Evaluation results default to the user directory in the examples.

```bash
# Run from this directory.
bash scripts/fetch-models.sh all
dotnet test tests/TranslationPoc.Tests.csproj
dotnet build TranslationPoc.csproj -c Release

ROOT="$HOME/.cache/translation-poc"
OUT="$HOME/VoicePoc/translation-eval"
mkdir -p "$OUT"
"$ROOT/runtime-b11980/llama-b11980/llama-server" \
  --model "$ROOT/models/Hy-MT2-7B-Q4_K_M.gguf" \
  --host 127.0.0.1 --port 8097 --ctx-size 8192 --parallel 1 \
  --n-gpu-layers 99 --threads 6 --metrics --reasoning-budget 0
```

Keep the server in its own terminal; stop it when the experiment finishes. Run these commands in a second terminal:

```bash
H="dotnet bin/Release/net10.0/TranslationPoc.dll"
$H translate --target en --text "这个 PR 先不要合并，等 CI 跑完。"
$H bench --corpus cases.json --out "$OUT/hy7.jsonl" --repeat 3
$H replay --model hy-mt2-7b --strategy units --out "$OUT/units.json"
$H replay --model hy-mt2-7b --strategy batch --out "$OUT/batch.json"
$H replay --model hy-mt2-7b --strategy units --count 201 --interval-ms 3000 --out "$OUT/soak.json"
```

`--temperature 0.7` tests the model card's sampling recommendation; the deterministic comparison uses 0. Both use a fixed seed, top-p 0.6, top-k 20, repetition penalty 1.05, and a bounded output budget.

The llama.cpp release is pinned to b11980 / `f7c01c522`; model revisions and SHA256 hashes are in [the downloader](scripts/fetch-models.sh). It verifies all downloaded archives and weights. The provided bootstrap is macOS arm64-only; Windows is not yet exercised.

### Offline quality scoring

```bash
uv venv --python 3.12 "$ROOT/eval-env"
uv pip install --python "$ROOT/eval-env/bin/python" -r scripts/requirements-eval.txt
"$ROOT/eval-env/bin/python" scripts/score.py "$OUT/hy7.jsonl" --out "$OUT/summary.json"
```

`cases.json` contains 31 **authored development probes** and single-author reference translations. These are not an independent human-reviewed benchmark or hidden test set. chrF++ is reference similarity, not translation accuracy. Failed requests stay in the quality denominator as empty hypotheses. Literal checks cannot detect semantic reversals.

### Corpus evaluation (multi-source, reference-based)

```bash
python3 scripts/build-corpus.py            # needs the downloads described in the script header
scripts/run-final.sh                       # sequential LLM runs -> ~/VoicePoc/translation-eval/runs/<label>/<set>.jsonl
~/.cache/translation-poc/nmt-env/bin/python scripts/nmt-corpus.py --sets wmt24-en-zh wmt25-en-zh wmt23-zh-en iwslt17-zh-en ifmt probes
~/.cache/translation-poc/metric-env/bin/python scripts/score-corpus.py   # chrF++, COMET-22, MetricX-24, checks, IFMT rules
python3 scripts/report.py                  # report/results.md + review/index.html + review/review.csv
```

Results, models, limits and the decisions they changed are in [REPORT.md](REPORT.md). Runs and scores live outside the repository under `~/VoicePoc/translation-eval/`.

### Independent CPU NMT baseline

```bash
uv venv --python 3.12 "$ROOT/nmt-env"
uv pip install --python "$ROOT/nmt-env/bin/python" -r scripts/requirements-nmt.txt
PATH="$ROOT/nmt-env/bin:$PATH" "$ROOT/nmt-env/bin/python" scripts/nmt-benchmark.py \
  --corpus cases.json --out "$OUT/opus-ct2.jsonl" --repeat 3
```

The baseline converts the two pinned official OPUS-MT models to CTranslate2 int8 and uses six CPU threads with beam size four. It has no glossary or JSON/literal wrapper, so full-corpus numbers compare **candidate systems**, not only model weights. Semantic/language quality should also be reviewed on the plain-text subset. It does not import any product ONNX assets or decoder code.

## Transport and failure behavior

HTTP errors, truncated output, missing finish markers and missing/duplicated literal tokens are explicit failures. There are no application-level automatic retries. Initial runs observed native HTTP keep-alive `ResponseEnded` failures; changing Content-Length alone did not resolve them. Explicit fresh loopback connections eliminated the observed failures in later runs. This is a local POC transport policy, not a recommendation to disable connection reuse for cloud APIs.

The session is not a production-ready speech/translation SDK: results are retained in memory, no workload admission policy exists yet, and glossary/context cache persistence and user-owned translations remain future work.

See [the experiment report](REPORT.md) and [the standalone validation plan](../../docs/superpowers/plans/2026-10-29-translation-poc.md).
