# LiveLingo v1.0 Roadmap

> 基于 product-design.md 的实施路线图。

## 提案总览

| # | 提案 | 交付物 | 预估 | 依赖 |
|---|------|--------|------|------|
| P1 | Foundation & Architecture | 多项目结构 + 接口 + PoC 迁移 | 1 周 | 无 |
| P2 | Translation Core | 模型管理 + MarianMT + 语言检测 + 管线 | 2-3 周 | P1 |
| P3 | AI Post-processing | Qwen + LLamaSharp + 三种后处理器 | 2 周 | P2 |
| P4 | macOS Platform | 全平台服务 macOS 实现 | 2-3 周 | P1 |
| P5 | Settings & Polish | 持久化配置 + 首次引导 + 快捷键配置 | 1-2 周 | P3 |

## 时间线

```
        Week 1    Week 2-4       Week 5-6       Week 7-8      Week 9-10
        ──────    ────────────   ────────────   ───────────   ───────────
主线:   [  P1  ]  [    P2      ] [    P3      ] [    P5     ]
                                                               
并行:                            [       P4 (macOS)         ]
                                                               
里程碑:    ▲          ▲              ▲              ▲            ▲
         架构就位   真实翻译可用   AI润色可用    macOS可用    v1.0 发布
```

## 依赖图

```
  P1: Foundation ─────────────────────────────────────┐
    │                                                  │
    │  定义所有接口 + 迁移 PoC                           │
    │                                                  │
    ├──────────────────────┐                           │
    │                      │                           │
    ▼                      ▼                           │
  P2: Translation Core   P4: macOS Platform            │
    │  ModelManager          CGEventTap                │
    │  MarianMT/ONNX         AXUIElement               │
    │  FastText              NSWorkspace               │
    │  Pipeline                                        │
    │                                                  │
    ▼                                                  │
  P3: AI Post-processing                               │
    │  Qwen/LLamaSharp                                │
    │  Summarize/Optimize/Colloquialize                │
    │                                                  │
    ▼                                                  │
  P5: Settings & Polish  ◀────────────────────────────┘
      JSON 持久化
      首次运行引导
      可配置快捷键
```

## 各提案详情

见 proposals/ 目录下各提案文档：
- [P1: Foundation & Architecture](proposals/P1-foundation.md)
- [P2: Translation Core](proposals/P2-translation-core.md)
- [P3: AI Post-processing](proposals/P3-ai-postprocessing.md)
- [P4: macOS Platform](proposals/P4-macos-platform.md)
- [P5: Settings & Polish](proposals/P5-settings-polish.md)

## 里程碑定义

### M1: 架构就位 (P1 完成)
- [ ] LiveLingo.Core 项目创建，包含所有公开接口
- [ ] LiveLingo.App 中平台接口定义完成
- [ ] PoC Windows 代码迁移到平台接口实现
- [ ] DI 注册工作，stub 翻译仍然可用
- [ ] 功能与 PoC 完全一致

### M2: 真实翻译可用 (P2 完成)
- [ ] ModelManager 可下载/缓存/管理模型
- [ ] MarianMT 中→英翻译工作
- [ ] 语言自动检测工作
- [ ] 实时翻译管线 (文本变化 → 取消 → 重翻)
- [ ] 基础模型下载 UI

### M3: AI 润色可用 (P3 完成)
- [ ] Qwen2.5-1.5B 本地推理工作
- [ ] 三种后处理模式可切换
- [ ] 管线两阶段流转 (翻译 → 润色)
- [ ] 后处理自动跟随翻译结果

### M4: macOS 可用 (P4 完成)
- [ ] macOS 全局快捷键工作
- [ ] macOS 前台窗口识别工作
- [ ] macOS 文本注入工作
- [ ] 权限引导 UI

### M5: v1.0 发布 (P5 完成)
- [ ] 用户配置持久化
- [ ] 可配置全局快捷键
- [ ] 多语言对管理
- [ ] 首次运行引导完善
