# Third-party notices

LiveLingo 2.0 ships a small set of native libraries and downloads open models on the user's request. This file
records what each component is, who owns it and under which license it is used. Licenses were read from the upstream
repositories and model cards on 2026-10-31; re-check them when a pinned revision changes (the pins live in
`src/LiveLingo.Core/Models/ModelCatalog.cs`, which also shows each model's license in the download banner data).

This is an engineering inventory, not legal advice.

## Models (downloaded from their original hosts after the user agrees; never bundled)

| Model | Role | Source of the pinned file | License |
|---|---|---|---|
| Hy-MT2 1.8B / 7B, Q4_K_M | Translation | `tencent/Hy-MT2-1.8B-GGUF`, `tencent/Hy-MT2-7B-GGUF` (Hugging Face) | Apache-2.0 (model card and `LICENSE.txt`) |
| Qwen3.5 4B, Q4_K_M | Text cleanup | `unsloth/Qwen3.5-4B-GGUF`, derived from `Qwen/Qwen3.5-4B` | Apache-2.0 (model card links the Qwen license) |
| X-ASR 480 ms streaming zh-en, int8 | Live recognition | sherpa-onnx `asr-models` release; upstream `Gilgamesh-J/X-ASR` | Apache-2.0 (upstream `LICENSE`) |
| Qwen3-ASR 0.6B, int8 | Second pass and recording import | sherpa-onnx `asr-models` release; upstream `Qwen/Qwen3-ASR-0.6B` | Apache-2.0 (model card) |
| Silero VAD | Speech boundaries | sherpa-onnx `asr-models` release; upstream `snakers4/silero-vad` | MIT (upstream `LICENSE`) |

Every download is verified against a pinned SHA256 before it is used.

## Runtimes and libraries shipped with the app

| Component | Use | License |
|---|---|---|
| llama.cpp b11980 (`llama-server`) | Runs the translation and cleanup models; downloaded on demand | MIT |
| sherpa-onnx 2.0.1 (`org.k2fsa.sherpa.onnx`) with ONNX Runtime | Speech recognition | Apache-2.0 (sherpa-onnx), MIT (ONNX Runtime) |
| PortAudio via PortAudioSharp2 | Microphone capture | MIT |
| Avalonia 12 preview | UI | MIT |
| Preact 10.29.8 (`dist/preact.min.js`, sha256 `25aa00978fef96e4fd0020cc723a41c3b1a1e81c9c087eff26a77e07a4867e8e`) | Renders the composer and settings in the web view; vendored in `src/LiveLingo.Desktop/Web/Assets/vendor` | MIT |
| htm 3.1.1 (`dist/htm.js`, sha256 `80e39afe20fd61183412eda89efa10532d57945e6364642aceacd50eb2384b4b`) | Template-literal views for Preact; vendored next to it | Apache-2.0 |
| CommunityToolkit.Mvvm | View models | MIT |
| Velopack | Updates and installers | MIT |
| Serilog | Logging | Apache-2.0 |
| SharpCompress | Archive extraction | MIT |

## Project license

The repository has no `LICENSE` file yet, although the 1.x README displayed an MIT badge. The owner must choose and add
the license before a release. Nothing above restricts that choice: all components are permissive and require only that
their notices are kept, which this file does for the shipped binaries (include it in installers).

## Open points

- The Apache-2.0 components require a copy of the license text and any NOTICE files in distributions. The installers
  do not include them yet.
- Model licenses are those declared on the model cards. A later model revision may change them; the pinned commit
  hashes prevent that from happening silently.
