# Voice dictation POC

Validates the speech-to-final-text pipeline and the candidate local models. It is isolated from the main application's `src/` and includes a microphone test UI, but no global hotkey or text insertion into other applications.

## Design and next phase

- [Validated speech architecture and product migration gates](../../docs/superpowers/specs/2026-10-29-voice-dictation-design.md)
- [Text translation and live transcription-translation POC plan](../../docs/superpowers/plans/2026-10-29-translation-poc.md)

The design document distinguishes historical measurements from the current defaults and records the limits of the fidelity checks. In particular, KeSpeech's official license restricts commercial use, adaptations and redistribution; historical exploratory scores are not a product-release acceptance benchmark. Review the original terms before further use.

## Run

```bash
scripts/fetch-models.sh        # pinned models into ~/.cache/voice-poc/models (about 5 GB)
scripts/build-corpus.sh        # synthetic corpus with macOS `say` into corpus/audio
dotnet test tests/Dictation.Core.Tests
dotnet build src/Dictation.Harness -c Release
H="dotnet src/Dictation.Harness/bin/Release/net10.0/Dictation.Harness.dll"
$H run    --second xasr --cleaner qwen35-4b --glossary on --out results/run.json   # incremental pipeline, real-time replay
$H serial --second xasr --cleaner qwen35-4b --glossary on --out results/serial.json # baseline: wait until the end, then process
```

## Test app (kept permanently)

A unified Avalonia workbench (`src/Dictation.TestApp`) with two tabs to test both validated POC pipelines by hand:

1. **1. 语音识别与整理 (Voice Dictation)**: speak, watch the raw text and the cleaned draft grow, press Stop (Cmd/Ctrl+Enter) to commit, copy the result, or click **Send to Translation →** to send the recognized text directly to the translation workbench.
2. **2. 实时文本翻译 (Text Translation)**: auto-starts the local `llama-server` (`hy-mt2-7b` by default, switchable to `hy-mt2-1.8b`), lets you switch translation direction (`zh → en`, `en → zh`) and output contract (`json (schema)` vs `text (plain)`), load 6 built-in verification presets, test incremental sentence caching (`+ Append sentence (测前缀复用)`), inspect per-unit latency and protected literals, and save translation test cases to `~/VoicePoc/translation-records`.

```bash
dotnet run -c Release --project src/Dictation.TestApp
```

- The first start loads the models (about 10 s); the button is disabled until then. The microphone permission prompt on macOS appears for the terminal that launched the app.
- The tested defaults are X-ASR streaming (480 ms), Qwen3-ASR-0.6B for the second pass, Qwen3.5-4B for cleanup, and a Silero VAD threshold of 0.35. The second-pass dropdown still allows comparisons with `xasr`, `funasr`, `sensevoice`, or `none`.
- The app references the same core pipeline as the replay harness: second-pass audio coverage and boundary fixes, model-token removal, repetition-loop detection, and cleanup fidelity checks (including quote/bracket balance) apply to microphone takes too. Rebuild and restart after changing the core; an already running window keeps its loaded version.
- **Save audio** is on by default. Each take is stored as `<id>.wav` + `<id>.json` (raw, cleaned, per-sentence states, rejected texts, glossary, timings) in `~/VoicePoc/recordings` (override with `VOICE_POC_RECORDINGS`). This folder is in the user profile and never inside the repository. Audio is written while recording, so a crash still leaves a valid file.
- Use the recordings-folder box or **Browse...** to choose where new takes are saved; existing recordings stay in their original folder.
- Type what you really said into the **reference** box after a take and press Save reference. That turns the take into a measurable test case.
- A level bar and a warning show when the OS delivers silence (missing microphone permission).
- Re-run saved takes through the current pipeline, in real time, with the harness: `Dictation.Harness replay [--takes id1,id2] [--recordings <dir>]`. `Dictation.Harness mic-check` records 3 s and prints the level.
- Output goes to the Copy buttons only. Inserting into the foreground application is not part of the POC.

## Pipeline under test (`Dictation.Core.DictationSession`)

1. One continuous live stream (X-ASR streaming) gives text while the user speaks. It is append-only, so no stabilizer is needed.
2. Silero VAD only proposes speech spans. The session groups them into utterances. A pause of at least 800 ms with no speech in progress ends the utterance and sentence and resets the live stream. A shorter pause never resets it. A 12 s utterance is cut softly at the next short pause.
3. Each closed utterance is re-decoded by the selected second-pass model (Qwen3-ASR-0.6B by default in the test app) for the settled text.
4. Settled text is split into sentences. Each sentence is cleaned by the local LLM (Qwen3.5-4B) as soon as something follows it, so cleanup runs during speech.
5. A deterministic fidelity gate accepts or rejects every cleanup. A rejected sentence keeps its raw text.
6. Enter commits: the remaining tail, second pass and cleanup are drained, then the text is returned.

## Measured results

Machine: M3 Pro, 36 GB. Audio is synthetic macOS TTS replayed at real-time pace. Treat all accuracy numbers as relative; they say nothing about real speech.

| Scenario | Audio | Live CER | Settled raw CER | Clean lag p50 / p95 | Commit wait | Sentences cleaned / rejected | Backlog |
|---|---|---|---|---|---|---|---|
| doc-longform | 81 s | 17.6% | 6.2% | 3.5 s / 4.7 s | 1.5 s | 13 / 1 | 40 ms |
| doc-soak | 322 s | 17.9% | 6.7% | 3.4 s / 4.6 s | 1.4 s | 54 / 2 | 40 ms |
| meeting-chat | 12.6 s | 10.4% | 6.2% | 2.7 s / 3.2 s | 1.6 s | 2 / 0 | 0 ms |

- Live retractions: 0 in every run. First text appears about 0.5 to 1.2 s after speech starts (p50).
- Glossary stress with 8 irrelevant terms: 0 false insertions in every scenario.
- Lag did not drift over the 5.4 minute soak.

Baseline A (serial: record, then recognize and clean everything after speech ends):

| Scenario | Audio | Wait after speech, serial | Wait after speech, incremental |
|---|---|---|---|
| meeting-chat | 12.6 s | 1.9 s | 1.6 s |
| doc-longform | 81 s | 8.9 s (0.97 recognize + 7.9 clean) | 1.5 s |
| doc-soak | 322 s | 12.9 s (3.8 recognize + 9.2 clean) | 1.4 s |

Serial wait grows with dictation length; incremental wait does not. Extrapolating the serial cleanup rate (about 1 s per 10 s of speech), a 10 minute dictation would wait roughly 25 s. That extrapolation is not measured.
Serial raw CER is higher than incremental (10.1% vs 6.2% on doc-longform) because it uses 250 ms VAD spans instead of hard-pause utterances.
The serial one-shot cleaner collapsed the soak text from 1578 to 386 characters. The soak audio is one paragraph repeated four times, so the model de-duplicated it. This is a test artifact and not evidence about natural speech.

Second-pass recognizer sweep (8 scenarios, per-VAD-segment design, mean CER): X-ASR offline 23.3% (57 ms median per segment), Qwen3-ASR-0.6B 22.5% (453 ms), Fun-ASR-Nano 28.4%, SenseVoice 28.9%, live stream only 34.1%. X-ASR offline is the efficient choice.

Context size for cleanup (doc-longform): with 2 preceding sentences one sentence was rejected for copying the context; with 0 one was rejected for dropping a clause. Both were caught by the gate. Kept at 2.

## Real-speech evaluation (use this, not the synthetic corpora)

The synthetic corpora below (macOS `say`, edge-tts voices) are clean and uniform. They check pipeline timing and behavior only. Accuracy is judged on **real human recordings with human transcripts**:

| Source | Content | Clips | License note |
|---|---|---|---|
| KeSpeech test (`TwinkStart/KeSpeech`) | read speech, Mandarin + 8 sub-dialects, 6 sentences joined per clip, about 35 to 60 s | 23 | free for academic use under its own agreement; keep local |
| WenetSpeech `test_meeting` | real meetings, one 7 to 12 s utterance per clip | 20 | research use; keep local |
| WenetSpeech `test_net` | internet video, podcasts, interviews, consecutive sentences, about 30 to 45 s | 10 | research use; keep local |
| ASCEND test (`CAiRE/ASCEND`) | spontaneous Mandarin-English conversation, about 25 s | 8 | CC-BY-SA-4.0 |

```bash
python3 scripts/fetch-real-corpus.py            # to ~/VoicePoc/reference-corpus (about 55 MB); refuses to write inside the repository
$H run --corpus ~/VoicePoc/reference-corpus/kespeech --second qwen3 --cleaner none --pace 4
```

The script reads rows through the Hugging Face datasets-server API (no multi-GB download), backs off on HTTP 429, drops annotation tags such as `<SPOKEN_NOISE>` and `[UNK]` from the references, and joins consecutive utterances with their natural gaps.

Settled CER after the second pass, final code (VAD threshold 0.35), one run per cell, so a point or two is noise. References are unpunctuated and contain fillers; WenetSpeech transcripts also carry some label errors, so absolute numbers are not comparable to published ones:

| Second pass | KeSpeech 8 accents | Meeting | Video | zh-en | Mean |
|---|---|---|---|---|---|
| Qwen3-ASR-0.6B (default) | 8.1% | 9.9% | 10.1% | 17.7% | 11.5% |
| X-ASR offline | 15.3% | 9.8% | 9.5% | 13.5% | 12.0% |
| Fun-ASR-Nano | 9.3% | 10.3% | 15.3% | 15.2% | 12.5% |
| SenseVoice | 13.6% | 11.2% | 13.2% | 14.6% | 13.1% |
| Live stream only (X-ASR streaming) | 24.2% | 12.8% | 12.6% | 15.8% | 16.3% |

- Qwen3-ASR-0.6B stays the default because of the accents (about half the error of X-ASR offline on KeSpeech). On the other three sets the recognizers are close. X-ASR was trained on a very large corpus that may overlap WenetSpeech; its meeting and video numbers may be optimistic.
- The real recordings exposed three pipeline defects that the synthetic audio hid. Each has a unit test that fails without the fix:
  1. **The second pass decoded only the detector's speech spans.** On far-field meeting audio the detector reported 0.5 s of an 8 s sentence, so the second pass replaced the correct live text with `落实好消防。`. It now decodes the audio the live stream consumed. Meeting CER 23.0% to 9.4%.
  2. **A recognizer decoding trailing silence invents words** (`然后。`), and padding across the previous cut repeated boundary words (`首先。首先,`). The decoded audio now ends at the last speech evidence (detector span end or last live text change, plus 300 ms) and never reaches back before the previous cut.
  3. **Qwen3 emits `<|endoftext|>` and keeps hallucinating** (`Human Rights Watch…`). Text after it is dropped and model tokens are stripped.
- Silero VAD threshold 0.5 to 0.35 to 0.25 on meeting, video and zh-en with Qwen3: 9.3 / 11.2 / 16.7, then 8.8 / 10.6 / 14.7, then 8.2 / 10.9 / 14.1. A lower threshold helps a little everywhere; 0.35 is the default and `--vad-threshold` overrides it.
- The synthetic accent corpus did not regress (Qwen3 settled CER 8.4%, was 9.2%).

Cleaner on real speech (Qwen3 second pass + Qwen3.5-4B, real-time replay of the video and zh-en sets, 208 sentences, no glossary): 59 sentences changed, 14 rejected by the fidelity gate, mean commit wait 2.9 s on video (max 5.9 s) and 2.0 s on zh-en (max 2.9 s).

- Good edits: fillers and stutters removed (`就恨不得啊，用那个眼神啊` to `就恨不得用那个眼神`), homophones fixed (`按耐` to `按捺`), digits spaced (`100多年` to `100 多年`).
- The gate stopped real faithfulness failures: translating a Chinese sentence into English (`What's the major? …`), translating `play basketball` to `打篮球`, inventing words (`tough`, `tutor`, `had`), and silently deleting words (`一个`, `快也是`).
- Many edits only drop the sentence-final period of a fragment that continues; this is intended.
- One defect got through: the model dropped a closing quotation mark (`…嘛。”` to `…嘛。`). The gate now rejects output that unbalances quotes or brackets (unit-tested, red without the rule).
- Raw 10.1% to final 10.5% (video) and 17.7% to 18.2% (zh-en): the reference keeps fillers, so cleaning cannot lower CER here. Fidelity was judged by reading the changed and rejected sentences, not by CER.

Not covered by this evidence: technical terms and API names spoken by real people (none of these sets contain them; use your own takes from the test app with a reference), long dictation beyond about one minute with real speech, and the cleaner on real speech (the runs above used `--cleaner none`).

## Accent evaluation with synthetic voices (pipeline checks only)

Generates the same scenario text with voices that approximate regional accents, runs the pipeline on them, and reports accuracy per accent.

```bash
uv tool install edge-tts                                  # once
scripts/build-dialect-corpus.sh                           # variants in corpus/dialects.json -> corpus-dialect/audio (needs network)
$H run --corpus corpus-dialect --second xasr --cleaner qwen35-4b --glossary on   # prints a per-voice table and a scenario x voice matrix
```

Voices: standard Mandarin female/male (baseline), Northeastern (Liaoning), Shaanxi, Taiwan Mandarin female/male. Only the synthetic scenario text goes to Microsoft's online voices; no user speech or text is involved. `scripts/build-corpus.sh` is the single builder; the dialect script only sets `ENGINE`, `VOICE`, `ID_SUFFIX` and `OUT_DIR`.

Limits: these are neural voices, not recordings of real dialect speakers. Microsoft offers only two dialect voices (Northeastern, Shaanxi). More dialects need Fun-CosyVoice3 (18+ dialects by text instruction, Apache-2.0, local), which needs about 7 GB of disk and was not set up here. For a ground truth with real speakers, use a recorded Mandarin sub-dialect corpus such as KeSpeech (not run; license and download not checked).

Mean settled CER over 8 scenarios (48 clips, one run each, so differences of a few points are noise). `numbers-versions` is dominated by digit formatting (the reference spells numbers in Chinese), so the second table leaves it out.

| Recognizer (second pass) | baseline f | baseline m | Northeast | Shaanxi | Taiwan f | Taiwan m |
|---|---|---|---|---|---|---|
| X-ASR streaming (live only) | 6.6% | 8.0% | 8.1% | 36.2% | 11.6% | 18.0% |
| X-ASR offline (current choice) | 6.8% | 6.7% | 8.0% | 25.9% | 10.6% | 14.9% |
| Qwen3-ASR-0.6B | 7.3% | 6.1% | 6.3% | 11.5% | 8.1% | 16.2% |
| SenseVoice | 6.9% | 8.7% | 9.0% | 28.0% | 11.7% | 17.6% |
| Fun-ASR-Nano | 6.7% | 6.9% | 8.0% | 109.0% | 7.7% | 11.9% |

Without `numbers-versions`: X-ASR offline 3.3 / 3.2 / 4.8 / 20.9 / 6.7 / 9.7; Qwen3-ASR-0.6B 2.8 / 2.5 / 2.8 / 8.0 / 4.2 / 11.2; Fun-ASR-Nano 2.9 / 3.1 / 3.3 / 3.7 / 3.7 / 6.3.

- On clean standard Mandarin all recognizers are within noise. They differ on accents: with the Shaanxi voice the current second pass (X-ASR offline) is about 3x worse than Qwen3-ASR-0.6B and Fun-ASR-Nano. Accents are where the current choice is weakest.
- Fun-ASR-Nano is the best on accents but fell into a repetition loop on one clip (`numbers-versions@shaanxi`, 334 characters for 51 spoken, hence 109%). It needs a length-versus-duration guard before it can be the second pass. Qwen3-ASR-0.6B has no such failure here.
- Accent errors are phonetic substitutions ("不要加重试" became "表甲虫师"). The cleaner has no audio, so it cannot repair them. Negation can be lost in recognition before cleanup sees it.
- The cleaner repaired terms and some homophones (`联条的排齐` to `联调的排期`, `avalonde` to `Avalonia`). On accent text it also made one wrong guess that the gate accepted: `补一个` became `部署一个`. Catching this needs a pinyin similarity check on substituted spans; not built.
- The run exposed a prompt-layout echo ("词表：… 目标：…" prefixed to a sentence) that the gate accepted. The gate now rejects multi-line output (unit test added, verified to fail without the rule). The run above predates that fix.

### Second-pass choice after the accent run

`SessionOptions.SecondPassMaxCharsPerSecond` (15) drops a second-pass result with more characters than anyone can speak in that audio: a decoder repetition loop. Unit-tested, and the test fails without the rule. With it, Fun-ASR-Nano on `numbers-versions@shaanxi` goes from 109% to 10.9% mean CER on that voice.

Full pipeline (second pass + Qwen3.5-4B cleaner, 48 accent clips, real time):

| Second pass | Raw CER, no numbers scenario | Punctuation per 100 CJK chars | Worst / mean commit wait |
|---|---|---|---|
| X-ASR offline | 8.1% | 9.5 | 2.2 s / 1.6 s |
| Fun-ASR-Nano | 3.8% | 2.4 | 9.4 s / 2.0 s |
| Qwen3-ASR-0.6B (default) | 5.3% | 10.1 | 3.2 s / 1.9 s |

Fun-ASR-Nano is the most accurate but emits almost no punctuation (sentence splitting then relies on length limits) and one clip needed a 9.4 s commit. Qwen3-ASR-0.6B is the default. Fun-ASR-Nano stays selectable in the test app. One run per cell; treat gaps of a few points as noise.

CER after cleaning is not a quality measure here: the reference keeps fillers and self-corrections that the cleaner correctly removes, so final CER rises (for example `self-correction` 0% raw to 14.6% final).

Where audio is stored (none of it is in git):

- `corpus/audio/`: macOS `say` standard-Mandarin corpus (wav + timeline.json).
- `corpus-dialect/audio/`: accent corpus, `<scenario>@<variant>.wav` + `.timeline.json`.
- `corpus/.work/edge-cache/`: raw edge-tts mp3 cache.
- `~/VoicePoc/recordings/` (override with `VOICE_POC_RECORDINGS` or the test app's folder box): takes recorded by hand, `<id>.wav` + `<id>.json`.

## Design bugs found by real runs (each pinned by a unit test)

- Streaming X-ASR does not punctuate at segment end, so sentence boundaries come from the second pass and the splitter.
- Resetting the live stream at every short pause wrecked the next words; reset only at hard pauses (meeting-chat live CER 37.5% to 10.4%).
- VAD reports spans only after they end, so `IsSpeechActive` is required to tell a pause from speech in progress.
- The cleaner copied read-only context into its output when it was given following text; it now receives only a `Continues` flag.
- The cleaner invented digits from garbled numerals and silently deleted garbled words. The gate now rejects invented numbers and any deletion that is not a filler, stutter, self-correction, single noise character, or glossary-explained garbage.

## Not verified

- Windows: no machine was available. Only macOS arm64 was run.
- Real microphone capture, global hotkeys, hold/lock gestures, target-window refocus, text insertion into real apps, IME composition.
- Real human speech accuracy, and any comparison against WeChat or Doubao. The corpus is synthetic TTS, and TTS mispronounces English terms (for example Avalonia). Terms and numbers scenarios stay poor for every recognizer (live CER 64% to 73%).
- Qwen3-ASR-1.7B (only 0.6B was run), Fun-ASR-Nano as a streaming engine, X-ASR live chunk sizes other than 480 ms, the control cleaner Qwen3-4B-Instruct-2507.
- Cloud (Foundry) path.
- Whether the gate's explained-deletion whitelist is too strict for natural speech; the acceptance rates above come from the synthetic corpus only (96% on the soak).
- Unrecorded in the corpus: glossary in doc-soak does not contain macOS, so two "Max Oce" sentences were rejected there.
