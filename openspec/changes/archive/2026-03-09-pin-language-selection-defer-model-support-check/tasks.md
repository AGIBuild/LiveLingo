## 1. 固定语言目录基础设施

- [x] 1.1 新增 `ILanguageCatalog`/`LanguageCatalog`，定义固定语言清单与稳定顺序（zh/en/ja/ko/fr/de/es/ru/ar/pt）；AC: 能在单测中断言数量、顺序与代码值一致
- [x] 1.2 在应用 DI 注册语言目录单例并补充构造注入路径；AC: `SetupWizardViewModel`、`SettingsViewModel`、`OverlayViewModel` 均可解析到目录实例

## 2. ViewModel 行为解耦改造

- [x] 2.1 `SetupWizardViewModel` 改为使用目录语言列表，移除本地重复语言定义；AC: 向导语言下拉来源仅为目录
- [x] 2.2 `SettingsViewModel` 改为使用目录语言列表，移除 UI 语言选择对 `ITranslationEngine.SupportedLanguages` 的依赖；AC: 设置页源/目标语言可见项与目录完全一致
- [x] 2.3 `OverlayViewModel` 改为使用目录语言列表并移除翻译前 `SupportsLanguagePair` 主动阻断；AC: 不支持语对时仍会调用 pipeline，错误走既有失败路径

## 3. 视图绑定与文案一致性

- [x] 3.1 更新 Wizard/Settings/Overlay 绑定，确保都消费对应 ViewModel 的目录语言数据与本地化文案；AC: 三处界面可选语言一致且不随模型状态变化
- [x] 3.2 校准“不支持/未下载模型”错误提示文案为可操作引导（去设置下载模型）；AC: 失败提示不再出现前置支持性拦截语义

## 4. 验证与回归

- [x] 4.1 新增/更新单测：目录服务、ViewModel 数据源、Overlay 非预检行为；AC: 覆盖“可选语言固定”“不支持语对仍触发翻译尝试”两个核心场景
- [x] 4.2 执行桌面端受影响测试集并修复回归；AC: 相关测试全部通过且无新增 lint 错误
