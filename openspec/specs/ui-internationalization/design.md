## Context

当前 `LiveLingo.App` 的 UI 文案分散在 `App.axaml.cs`、`SettingsWindow.axaml`、`OverlayWindow.axaml`、`NotificationToast.axaml` 等文件中，几乎全部硬编码英文。托盘菜单、About/更新弹窗、设置页标签、浮动翻译窗口提示都未经过统一资源管理。随着功能增长，文案维护、翻译协作、回归验证都越来越困难。

## Goals / Non-Goals

**Goals:**
- 建立统一本地化能力：通过 key 获取文本、支持格式化参数、支持默认回退语言
- 将核心 UI 壳层（Tray/Menu/Dialog/Settings/Overlay/Toast）文案迁移到资源文件
- 提供 UI 语言偏好设置并持久化，在应用启动时生效
- 允许在运行时刷新主要入口文案（新开窗口与托盘菜单立即生效）

**Non-Goals:**
- 不引入在线翻译平台或第三方 i18n SaaS
- 不改造 Core 业务层的领域模型文本
- 不做全量多语言发布（本次至少支持 `en-US` 与 `zh-CN`）

## Decisions

### 1) 采用资源字典 + 本地化服务

**决策**：新增 `ILocalizationService`（App 层），提供：
- `string T(string key)`
- `string T(string key, params object[] args)`
- `CultureInfo CurrentCulture`
- `void SetCulture(string cultureName)`

并以 JSON 资源文件作为后端：
- `Resources/i18n/en-US.json`
- `Resources/i18n/zh-CN.json`

**原因**：避免分散硬编码，支持参数化文案（如版本号、更新版本）。

### 2) 本地化 key 命名规范

**决策**：使用分层 key 命名，避免冲突：
- `tray.openTranslator`
- `tray.settings`
- `tray.checkUpdates`
- `tray.about`
- `tray.quit`
- `dialog.about.title`
- `dialog.update.available`
- `settings.general.hotkeys`

**原因**：便于检索、审查和测试覆盖。

### 3) DI 注册策略

**决策**：
- `ILocalizationService` 注册为 `Singleton`
- `ISettingsService` 已有，`LocalizationService` 启动时读取 `settings.UI.Language`
- App 初始化完成后调用 `localization.SetCulture(...)`

**依赖关系**：
```
JsonSettingsService -> UserSettings.UI.Language
                     -> LocalizationService.CurrentCulture
                     -> App/Views/ViewModels 文案解析
```

### 4) 运行时生效策略

**决策**：
- 托盘菜单：语言变更后重建菜单
- 窗口类（Settings/Overlay/About/Toast）：新开窗口按当前语言渲染
- 已打开窗口：本次不强制全量热更新（减少复杂度），但关键弹窗入口即时生效

### 5) 语言偏好配置

**决策**：在 `UISettings` 新增 `Language` 字段，默认 `en-US`，可选 `en-US`/`zh-CN`。

## Risks / Trade-offs

- **[遗漏硬编码文本]** → Mitigation: 用静态检索（`Text="..."`、`new NativeMenuItem("...")`）建立迁移清单
- **[key 缺失导致空文本]** → Mitigation: `LocalizationService` 先查当前语言，再回退 `en-US`，最后回退 key 本身
- **[运行时切换一致性]** → Mitigation: 托盘和弹窗路径优先支持即时生效，复杂窗口采用“重新打开生效”
- **[测试维护成本]** → Mitigation: 为 service 增加资源回退单测、为 App 菜单文本增加关键 smoke 断言

## Migration Plan

1. 新增 `ILocalizationService` 与 JSON 资源文件（en-US/zh-CN）
2. 在 `UISettings` 增加 `Language` 并加载默认值
3. 替换托盘菜单、About/更新弹窗文本为 `T(key)` 调用
4. 替换 Settings/Overlay/Toast 关键文案绑定
5. 增加设置项（语言选择）并保存
6. 补测试并执行全量构建测试

## Open Questions

- 语言切换是否需要“立即刷新当前 Settings 窗口内全部控件”还是“关闭重开生效”？
- `zh-CN` 是否需要简体/繁体分离（`zh-CN` / `zh-TW`）在本期纳入？
