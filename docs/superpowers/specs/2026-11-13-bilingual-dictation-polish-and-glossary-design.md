# LiveLingo · 中英混说识别增强、润色门禁修复与双角色切换提速设计

日期：2026-11-13。状态：**已实施并通过单元、浏览器与真实模型探针验证**。相关：[语音识别设计](./2026-10-29-voice-dictation-design.md)、[整理效果与窗口缩放设计](./2026-11-02-passage-cleanup-and-resize-design.md)、[模型预热设计](./2026-11-02-model-warmup-design.md)。

## 1. 真人问题样本与四层根因

以一段 437 字的真实研发口述（同时包含技术名词 `Agent / Skill`、口头停顿、口吃 `原原原话 / 才才能`、同音/近音误识别 `语音输入样例→压力 / 润色→用色 / Skill→Scale`）对照追踪全链路，定位到四个相互放大的根因：

### 1.1 润色几乎不生效：提示词与 `FidelityGate` 自相矛盾

[PolishStyle.Default](../../../src/LiveLingo.Core/Cleanup/PolishStyle.cs) 与 [LlmDictationPolisher](../../../src/LiveLingo.Core/Cleanup/LlmDictationPolisher.cs) 已经明确要求“接上碎句、去口头语、结合上下文修正同音与技术词误识别”，但模型按要求改写后会立刻触发 [FidelityGate](../../../src/LiveLingo.Core/Dictation/FidelityGate.cs) 的误拒：

1. **同篇已出现英文词或词表复现计数缺陷（`TermLost`）**：旧逻辑用 `!rawNorm.Contains(g)` 计算词表替换配额。只要说话人在第一句已被正确识别过一次 `skill`，后文把被听错的 `Scale` 修回 `Skill` 时配额仍算作 `0`，整篇 437 字全文润色因 `TermLost("scale")` 直接被一票否决。
2. **含“一”固定虚词被当成计量数字锁死（`Deleted`）**：`SpokenDigits` 将 `"一定的"`、`"一般"`、`"一些"`、`"一点"` 等非计量词中的 `"一"` 当成不可精简的数字字符，删掉 `"一定的"` 即判无故删字。
3. **正反问改写与多连字口吃漏判**：口语 `"会不会 / 是不是 / 有没有"` 润色为等价疑问 `"是否"` 时，字符级 LCS 把 `'不'→'否'` 拆成“删否定字 `'不'`”而拒绝；三连字口吃 `"原原原话"` 因删除长度 2 与剩余长度 1 不等长，未被 `IsRepetition` 识别为重复。
4. **全文被拒后退回单句孤岛**：`PolishScheduler` 被拒后退回逐段预览，`CleanupScheduler` 整段被拒后又按伪句号拆成 7～20 字的碎句逐条送检，彻底丧失跨句缝合与上下文纠错能力。

### 1.2 中英混说与固定口音误识别：零默认热词 + 翻译对与纠错规则混同

1. 二遍识别模型 `Qwen3-ASR-0.6B` 原生通过 `<|im_start|>system` 提示头接收 `Hotwords` 偏置，但产品默认词表为空，导致研发场景高频词（`Skill / LiveLingo / init`）在切片无上下文解码时易退化为 `Scale / 六零九 / 隐秘`。
2. `Qwen3-ASR` 的 `max_total_len` 由提示头与音频共享 KV 容量；无上限注入长词表会截断音频 token。
3. 旧 [Glossary](../../../src/LiveLingo.Core/Translation/Glossary.cs) 只有 `原词=译法`（翻译词对）一种结构，且将左侧 `Source` 注入语音热词与保护词；用户若按直觉填入 `Scale=Skill` 或 `六零九=LiveLingo`，反而会把错误的识别词当成热词强化。

### 1.3 说话中途“回车换行”造成的三层理解错位

1. **800 ms 停顿 + 悬空虚词伪句号**：说话人在中英切换前思考 0.8～1.0 秒（如 `如果说你还需要更强的……Agent 润色描述`、`你的验证目标应该以……M2`、`对于……init 命令`），孤立二遍解码会在悬空介词/连词/结构助词末尾硬补句号 `。`，`DictationSession` 随即强制成句。
2. **说话态界面按块分行**：实时口述没有时间轴，但前端 `#src-live` 仍把每个单元渲染成 `display: flex` 块级行，视觉上每停顿一次就把半句话硬换行。
3. **翻译器读取未润色原话且丢弃前文上下文**：[TranslationScheduler](../../../src/LiveLingo.Core/Translation/TranslationScheduler.cs) 固定读取 `unit.Raw`（忽略已通过门禁的 `unit.Clean.Text`），且从未向 `TranslationRequest.Context` 传入前文，导致半句断行后逐段孤立硬翻。

### 1.4 “润色 ↔ 翻译”切换漫长等待

1. **单角色预热导致切换冷启动**：`ModelWarmup.OnSummon` 只预热起始输出对应的单个本地 `llama-server`，首次切换需现拉起另一子进程、加载数 GB 权重并分配 `ctx-size=8192` 的 KV 缓存（约 1.5～4.5 s）。
2. **中短口述多余的前置摘要调用**：≤ 800 字口述本身已整篇送入 `PolishAsync`，却仍先串行调一次 `SummarizeAsync`（白等 2～4 s），再叠加门禁误拒后的十余次逐句回退请求。
3. **本地翻译主动关闭 Prompt Cache**：`LlmTranslator` 沿用了翻译压测脚本的 `cache_prompt = false`，每次请求重复计算指令前缀。

---

## 2. 解决方案设计

### 2.1 中英混说最优效闭环：有界声学热词 + 内置研发词库 + 口音误识别纠错表

不引入破坏实时性的多轮 Agent/Skill 运行时，而是打通 **“声学偏置 → 整理/润色纠错 → 保真门禁放行 → 浮窗一键沉淀”** 闭环：

1. **内置中英研发术语库（默认开启，可一键关闭）**
   - [Glossary.BuiltInTerms](../../../src/LiveLingo.Core/Translation/Glossary.cs) 维护约 48 个高频中英软件与 AI 术语（`Agent, Skill, Prompt, Token, MCP, RAG, LLM, API, SDK, PR, PRD, GitHub, Copilot, Claude, Cursor, VS Code, LiveLingo` 等），由 `ComposerSettings.BuiltInGlossary`（默认 `true`）控制；48 个词全量参与整理、润色与 `FidelityGate` 校验。
   - **声学热词硬预算（对齐 `sherpa-onnx` 47-token 阈值）**：`qwen3-asr-0.6B-int8` 的 ONNX 解码器总 KV 上限为 `512`（`max_total_len=512`），且 `offline-recognizer-qwen3-asr-impl.cc:866` 在提示头热词超过 `47` 个 BPE token 时每次解码都会向 `stderr` 打印告警建议。[Glossary.SpeechHotwords](../../../src/LiveLingo.Core/Translation/Glossary.cs) 因此按保守 BPE token 估算严格限制在 **24 词 / 160 字符 / 36 估算 token** 以内（用户词与纠错目标词优先，最易听混的内置口语英文词紧随其后），彻底消除原生层 `stderr` 告警并保护长片段音频 KV 容量。
2. **显式区分「口音/常见误识别纠错（`spoken=>canonical`）」与「翻译术语对（`source=target`）」**
   - `Glossary` 新增 `Corrections`（语法 `Scale=>Skill`、`六零九=>LiveLingo`、`用色=>润色`，与 `settings.json` 现有单字段存储完全兼容）：
     - **识别热词与保护词表**：只将右侧 `Canonical` 加入 `Glossary.Terms`，左侧错误读音绝不污染热词。
     - **整理与润色确定性预归一化（实测关键）**：实测 `Qwen3.5-4B` 仅靠提示词指令同时执行多条纠错规则时会出现漏改（保留 `Scale`）或误删（把 `Agent用色描述` 删成 `Agent 描述`）。因此由 [Glossary.ApplyCorrections](../../../src/LiveLingo.Core/Translation/Glossary.cs) 在进入清理/润色模型前按最长优先与英文整词边界（`Scale` 不影响 `Autoscaling`）确定性替换为 `Canonical`，再交由模型顺句去口头语；草稿的 `unit.Raw` 原话保持不改动，「改用原话」仍回到真实识别原文。
     - **保真门禁**：对着原始 `unit.Raw` 校验时，显式豁免已生效纠错规则对应的英文词替换与汉字删改配额。
3. **浮窗行内编辑一键记入 + 设置页独立管理**
   - 用户在浮窗编辑某一句结果（如把 `Scale机制` 改为 `Skill机制`、把 `用色` 改为 `润色`、把 `六零九` 改为 `LiveLingo`）时，[Glossary.SuggestCorrection](../../../src/LiveLingo.Core/Translation/Glossary.cs) 自动按完整英文标识符边界或双字中文词边界提取单处替换候选，在编辑栏直接提供 **「保存并记入纠错：Scale → Skill」** 按钮，一键落盘到设置。
   - 设置页「语音与翻译」新增 **内置中英研发术语库** 开关与 **口音与常见误识别纠错** 列表，与原有翻译术语表分离展示、互不污染。

### 2.2 保真门禁（`FidelityGate`）四项精准修复

保持“数字不编造、否定不反转、未出现且不在词表的英文词不凭空引入、整句不无故删减”的全部安全边界不变，修复四类误判：

1. **词表净增命中与同篇近音英文词修复**：按完整标识符边界统计词表词在整理前后的净增出现次数（避免 `GitHub` 误计 `Git`，同时支持前文已出现 `skill` 时修复后文误识别的 `Scale`）；无词表时，对同篇已出现且辅音骨架一致（如 `scale/skill`、`cloud/claude`）的近音词替换放行。
2. **非计量“一”字固定词豁免数字锁与凭空新增否定拦截**：`SmallDrop` 对 `"一会儿 / 一下子 / 一定的 / 一定 / 一般 / 一些 / 一点 / 一直 / 一旦 / 一会 / 一下 / 一样 / 一边 / 一起 / 一共"` 等程度/时态虚词解除 `SpokenDigits` 拦截（仍保护 `"一个 / 一口 / 一套"` 等真实计数词并受 8% 轻微删减预算约束）；同时在字符 LCS 新增跨度上拦截凭空编造的否定字（如把 `"他不光要"` 改成反义的 `"他不仅不"`），对称守住正反双向极性。
3. **正反问与“是否”等价极性识别**：将 `X不X`（`会不会 / 是不是 / 能不能`）及 `有没有` 改写为 `"是否"` 识别为疑问极性保留，而 `"不要 → 要"` 等真实否定丢失继续严格拒绝。
4. **周期性多连字口吃识别与润色防摘要漂移**：`IsRepetition` 支持任意周期词素的 2 连、3 连及更多次重复折叠（如 `原原原话 → 原话`、`才才能 → 才能`）；`LlmDictationPolisher.Frame` 明确禁止缩写概括时间限定、程度与举例细节，并结合全文主题补全快读脱落的固定成语字（如 `死记背 → 死记硬背`）。

### 2.3 样本守恒切分、悬空半句不断行、实时口述行内流式排版与翻译上下文衔接

1. **换气捕捉、无停顿波谷样本守恒切分与悬空虚词挂起（`DictationSession` + `SherpaSpeechEngine` + `SherpaQwen3Recognizer`）**：
   - 默认 `HardPauseMs` 调至 `1000 ms`、`PassagePauseMs` 调至 `2500 ms`；`LiveVadMinSilenceSeconds` 调至 `0.24f`（240 ms，对齐 POC 250 ms 基线）以捕捉快语速讲课中的自然换气；`SherpaSileroVad` `maxSpeechSeconds` 设为 `15f`，交由会话层统一掌控切分而不提前在音节中间暗切。
   - **样本守恒律与 512 KV 窗口联防（根治 `GenerateText:913` 及词中硬切吞字/重字）**：
     - `SoftMaxUtteranceMs = 8000` **仅在 `!_vad.IsSpeechActive`（确已进入静音换气）时触发**，绝不在正在发音时劈开句子；
     - 连续 `HardMaxUtteranceMs = 12000` 无停顿（含底噪导致 VAD 常亮）时，不再截在流式滞后 `480–700 ms` 的 `_openLastTextSample`，而是在末尾 `[TotalSamples - 1200ms, TotalSamples - 80ms]` 搜索能量最低的 20 ms 波谷帧 `cut`：上一段二遍解码严格止于 `cut`，下一段严格起于 `cut`（零样本空洞、零跨界重复解码），并将 `[cut, TotalSamples)` 回灌给重建后的流式识别器以保持流式右上下文连续；无 VAD 停顿就按停麦时同样不按滞后的 `_openLastTextSample` 裁剪尾字；
     - `SherpaQwen3Recognizer.Recognize` 对超过 `13s` 的导入音频片段同样在能量波谷分块解码，并剥离非末尾子块因孤立解码产生的伪句号。
   - **精准约束伪句号（保持「原话」与「整理」正交）**：仅对 `CutMidSpeech` 强切边界及封闭语法悬空尾词（连词/介词/结构助词、悬空代词副词 `他就 / 我就`、独立指示限定词 `这种 / 有这种`；排除 `好的。/ 我不喜欢这种。/ 差很多。` 等完整短句）暂挂句末伪句号，若用户就此停麦则自动恢复句号。
2. **说话态连续排版**：无时间戳的实时听写给 `#src-live` 启用 `.prose` 行内流式展示，消除边说边跳行的视觉割裂；导入录音字幕仍保持带时间戳的分行布局。
3. **翻译衔接润色稿与只读前文**：`TranslationScheduler` 优先采用已通过门禁或人工确认的 `unit.Clean.Text`，并携带 `draft.PrecedingText(i, 300)` 作为只读背景，避免口吃错词直译与跨段断句错位。

### 2.4 “润色 ↔ 翻译”切换加速（面向 Apple M2+ / ≥16 GB 基线）

1. **单组口述免前置摘要**：原文 ≤ `MaxGroupChars`（800 字）且单组即可润色时，跳过冗余的 `SummarizeAsync`，直接省去一次完整 LLM 串行调用；跨多组长文仍保留先摘要再分段润色。
2. **Fast 档双角色错峰预热与 KV 缓存瘦身**：`LlamaServerHost` 默认 `ContextSize` 从 `8192` 收敛至 `4096`（KV 显存减半）；`ModelWarmup.OnSummon` 在本地 Fast 档（`Hy-MT2-1.8B`）下按 `语音 → 当前输出模型 → 另一本地输出模型` 顺序后台串行预热（7B Quality 档不额外预热另一大模型以防 16 GB 设备内存吃紧），消除首次模式切换的进程冷启动。
3. **启用本地单并发 `cache_prompt = true`**：清理、润色、翻译在本地 `llama-server`（`--parallel 1`）上复用指令前缀 KV 缓存；云端请求仍由 `OpenAiChatClient` 自动剥离本地采样扩展字段。
