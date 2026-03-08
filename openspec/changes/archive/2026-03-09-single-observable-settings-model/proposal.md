## Why

当前设置链路存在 `SettingsProfile`（持久化）与 `SettingsDraft`（编辑态）双结构并行，导致映射和同步逻辑分散，维护成本高且易出现状态偏差。需要收敛为单一可观察设置模型，用同一结构完成绑定、编辑与持久化，降低复杂度并提升一致性。

## What Changes

- 引入单一 `SettingsModel`（`ObservableObject`），替代 `SettingsProfile` + `SettingsDraft` 双结构。
- `ISettingsService` 改为管理 `SettingsModel`：提供 `Current`、克隆副本、原子替换提交能力。
- 设置窗口改为编辑 `SettingsModel` 的工作副本（同类型不同实例），Save 提交、Cancel 丢弃，不再做结构转换。
- 配置同步消息收敛为单一 `SettingsChangedMessage`（无 payload），订阅方从 `ISettingsService.Current` 拉取最新值。
- 清理过渡兼容代码（转发属性、旧消息、双结构映射逻辑）与对应测试。

## Capabilities

### New Capabilities
- `settings-single-observable-model`: 定义“单一可观察设置模型 + 编辑会话副本”的配置读写与同步机制。

### Modified Capabilities
- `user-settings`: 将原有 `UserSettings`/双结构要求更新为单模型生命周期与原子替换语义。

## Impact

- `LiveLingo.Desktop/Services/Configuration`: 设置模型定义、`ISettingsService`、`JsonSettingsService`。
- `LiveLingo.Desktop/ViewModels`: `SettingsViewModel`、`OverlayViewModel`、`SetupWizardViewModel`。
- `LiveLingo.Desktop/App.axaml.cs` 与 `Messaging`: 设置事件发布/订阅链路。
- `tests/LiveLingo.Desktop.Tests`: 设置服务、ViewModel、启动链路相关回归测试。
