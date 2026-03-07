## 1. Localization Foundation

- [x] 1.1 新增 `ILocalizationService` 与 `LocalizationService`，支持 `T(key)`、`T(key,args)`、`SetCulture`，并实现 `active -> en-US -> key` 回退链。验收：服务单测覆盖 key 命中、回退、格式化。
- [x] 1.2 建立资源文件：`en-US.json`、`zh-CN.json`，补齐托盘、弹窗、设置页、浮窗、通知所需 key。验收：资源加载无缺失 key 异常。
- [x] 1.3 在 DI 注册本地化服务，应用启动读取 `UI.Language` 初始化当前文化。验收：`App` 启动后可从 DI 获取服务且文化正确。

## 2. User Settings Language Preference

- [x] 2.1 在 `UISettings` 增加 `Language` 字段（默认 `en-US`），保持 JSON 兼容。验收：旧配置可加载，新配置可保存该字段。
- [x] 2.2 在设置页增加语言下拉（`English` / `简体中文`），保存后写入 `settings.json`。验收：保存后文件中 `ui.language` 正确。
- [x] 2.3 语言保存后重建托盘菜单并使新打开窗口使用新语言。验收：无需重启即可看到托盘文本切换。

## 3. Localize UI Shell

- [x] 3.1 替换托盘菜单硬编码文本：打开翻译、设置、检查更新、关于、退出。验收：中英文切换后菜单文案正确。
- [x] 3.2 本地化 About、检查更新、更新可用、更新失败弹窗文案。验收：所有弹窗标题/按钮/正文取自资源。
- [x] 3.3 本地化设置页关键标签与按钮、浮窗状态提示、通知 toast 文案。验收：中英文下显示正确且无硬编码残留。

## 4. Runtime Refresh & Stability

- [x] 4.1 实现语言切换后的运行时刷新策略：托盘立即刷新，已打开窗口可重开生效，关键入口即时生效。验收：切换语言后新开窗口为新语言。
- [x] 4.2 为缺失资源 key 增加日志告警（不阻断主流程）。验收：缺 key 时 UI 仍可用并输出告警日志。

## 5. Tests & Verification

- [x] 5.1 增加 `LocalizationService` 单测（命中/回退/格式化/无 key）。验收：新增测试全部通过。
- [x] 5.2 增加设置语言偏好持久化测试与托盘菜单文案 smoke 测试。验收：测试覆盖语言保存与菜单刷新路径。
- [x] 5.3 执行 `dotnet build` + 相关测试套件，确认国际化改造无回归。验收：构建通过、测试通过。
