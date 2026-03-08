## Context

当前设置系统虽然已经完成消息收敛，但仍保留双结构模型：`SettingsProfile` 用于持久化，`SettingsDraft` 用于编辑。该模式带来了以下问题：

- 同一字段在加载、保存、同步路径上重复映射，维护成本高。
- 变更时容易出现“持久化结构已改、编辑结构遗漏”的不一致。
- 测试需要同时验证两套结构，回归点增多。

本次变更目标是以单一可观察模型承载设置状态，同时保留 Save/Cancel 的编辑会话语义，不把“未保存变更”提前传播到运行时。

## Goals / Non-Goals

**Goals:**
- 用单一 `SettingsModel`（`ObservableObject`）替代 `SettingsProfile + SettingsDraft` 双结构。
- 设置编辑流程改为“同类型不同实例”：`Current` + `WorkingCopy`。
- 保存流程实现原子替换，确保订阅方只看到一致快照。
- 配置消息进一步收敛为单一 `SettingsChangedMessage`（无 payload）。
- 统一 DI 注册与读取路径，减少映射层代码。

**Non-Goals:**
- 不调整设置项业务语义（热键、语言、模型选择规则保持不变）。
- 不改动非设置域逻辑（翻译管线、注入实现、窗口布局）。
- 不引入外部状态管理框架（继续使用现有 MVVM + Messenger）。

## Decisions

### Decision 1: 单一设置模型 + 编辑副本实例

采用 `SettingsModel` 作为唯一设置数据类型，既用于持久化也用于 UI 绑定。  
`SettingsViewModel` 在打开时从 `ISettingsService.CloneCurrent()` 获取 `WorkingCopy`，Save 时调用 `Replace(WorkingCopy)`，Cancel 时丢弃副本。

**Rationale**
- 消除类型映射复杂度，新增字段只改一个类型。
- 保持“未保存变更不生效”的用户语义。
- 便于单点验证和序列化一致性测试。

**Alternatives considered**
- 保留 `Profile + Draft`：实现稳定但长期维护成本更高。
- 直接绑定 `Current`：实现最简单，但无法支持 Cancel 语义。

### Decision 2: 服务层提供克隆与替换 API（替代 Update mutator）

`ISettingsService` 从 `Update(Func<T,T>)` 升级为：
- `SettingsModel Current { get; }`
- `SettingsModel CloneCurrent()`
- `void Replace(SettingsModel model)`

同时保留 `LoadAsync/SaveAsync/SettingsChanged`。

**Rationale**
- 与编辑会话模型一致，API 语义清晰。
- `Replace` 比 mutator 更适合“整体提交”场景。
- 更容易做原子替换和事件广播边界控制。

**Alternatives considered**
- 继续使用 mutator：对细粒度更新友好，但不匹配窗口编辑副本流程。

### Decision 3: 消息不携带设置对象

`SettingsChangedMessage` 不带 payload。订阅方收到后从 `ISettingsService.Current` 拉取。

**Rationale**
- 避免可变对象跨消息通道传递导致误改。
- 消息职责单一：通知变化，而不是携带状态。

**Alternatives considered**
- 继续传递完整对象：调试方便，但会引入对象所有权和一致性问题。

### Decision 4: DI 注册策略

保持单例注册：

- `services.AddSingleton<ISettingsService, JsonSettingsService>()`
- `services.AddSingleton<IMessenger>(_ => WeakReferenceMessenger.Default)`

消费侧全部通过 `ISettingsService` 读取 `Current` 或克隆副本，不直接 new 设置对象。

**Rationale**
- 维持现有生命周期模型，避免并发和多实例状态分叉。
- 保证 App / Overlay / Settings 使用同一数据源。

## Risks / Trade-offs

- **[Risk] 克隆不完整导致副本共享引用** → Mitigation: 对集合与嵌套对象实现显式深拷贝，并添加 round-trip + clone isolation 测试。
- **[Risk] Replace 期间竞态（并发 Save）** → Mitigation: `JsonSettingsService` 保持 `SemaphoreSlim(1,1)` 串行化替换与落盘。
- **[Risk] 无 payload 消息引发订阅方读取时序问题** → Mitigation: 约束顺序为“先 Replace，再发送消息”，订阅方只在 UI 线程消费。
- **[Trade-off] 需要一次性更新测试与调用点** → Mitigation: 分层迁移（Service -> ViewModel -> App -> Tests），每层跑全量测试。

