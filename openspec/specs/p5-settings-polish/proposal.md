## Why

P1-P4 实现了完整的翻译功能和双平台支持，但所有配置都是硬编码的：快捷键固定为 Ctrl+Alt+T、翻译方向固定为 zh→en、注入模式状态不持久化。用户无法自定义行为，首次使用缺少引导。P5 添加配置持久化、首次运行向导、可配置快捷键和多语言对管理，使产品达到 v1.0 发布标准。

## What Changes

- 定义 `UserSettings` 数据模型，覆盖快捷键、翻译、后处理、UI、高级选项
- 实现 `JsonSettingsService`：JSON 文件持久化到 LocalAppData/Application Support
- 创建首次运行三步向导：语言选择 → 模型下载 → 快捷键确认
- 实现 `HotkeyRecorder` 自定义控件：录制用户按下的快捷键组合
- 支持快捷键字符串解析（`HotkeyParser`）和热更新
- 创建 Settings 窗口（四个 Tab: General, Translation, AI, Advanced）
- 添加多语言对管理和 Overlay 中的语言对切换
- 添加系统托盘图标（Windows）/ 菜单栏图标（macOS）
- Overlay 位置记忆

## Capabilities

### New Capabilities
- `user-settings`: UserSettings 数据模型 + JsonSettingsService 持久化 + 损坏恢复
- `setup-wizard`: 首次运行三步引导（语言选择、模型下载、快捷键确认）
- `settings-ui`: Settings 窗口（四 Tab）、系统托盘/菜单栏、Overlay 位置记忆
- `configurable-hotkeys`: HotkeyRecorder 控件、HotkeyParser 解析、快捷键热更新

### Modified Capabilities

(无现有 spec 需要修改)

## Impact

- **文件系统**: 新增 `settings.json` 在 `%LOCALAPPDATA%\LiveLingo\` 或 `~/Library/Application Support/LiveLingo/`
- **启动流程**: 首次启动走向导流程，非首次直接加载配置
- **快捷键**: 从硬编码改为用户可配置，热更新无需重启
- **Overlay**: 状态栏增加语言对选择器，位置可拖动并记忆
- **系统托盘**: 新增托盘图标 + 右键菜单（Settings, Quit）
- **NuGet**: 无新增依赖（使用 `System.Text.Json` 内置包）
