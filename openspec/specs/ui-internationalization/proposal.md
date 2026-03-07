## Why

当前界面文案全部硬编码英文，托盘菜单、设置窗口、浮动翻译窗口、通知与更新提示都无法根据用户语言切换。国际化是产品可用性的基础能力，且后续功能迭代会持续增加文案，如果不先建立统一 i18n 机制，维护成本会快速上升。

## What Changes

- 在 `LiveLingo.App` 引入统一本地化服务（按 key 取文案，支持占位符）
- 建立资源文件结构（至少 `en-US` 与 `zh-CN`），替换 UI 中硬编码字符串
- 托盘菜单、About、检查更新弹窗、设置窗口、浮动窗口、通知 toast 全部接入本地化
- 新增语言偏好设置项并持久化，应用启动按设置加载语言
- 支持运行时刷新主要 UI 文案（Settings/Tray/Overlay 新建窗口生效）

## Capabilities

### New Capabilities
- `ui-localization-service`: 提供统一 i18n 服务、资源加载与格式化能力
- `localized-ui-shell`: 托盘、对话框、通知、窗口文案全面改为资源驱动
- `language-preference-setting`: 语言偏好设置与持久化加载能力

### Modified Capabilities

## Impact

- `LiveLingo.App`: `App.axaml.cs`（托盘菜单/弹窗文案）、`SettingsWindow.axaml`、`OverlayWindow.axaml`、`NotificationToast` 文案绑定改造
- 配置模型：`UserSettings` 增加 UI 语言字段（如 `Ui.Language`）
- 服务层：新增 `ILocalizationService` 及实现，DI 注册
- 测试：增加本地化服务单测、关键 UI/ViewModel 文案绑定测试、语言偏好持久化测试
