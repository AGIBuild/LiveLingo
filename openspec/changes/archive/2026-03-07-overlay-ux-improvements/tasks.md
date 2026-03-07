## 1. ViewModel 交互增强

- [x] 1.1 在 `OverlayViewModel` 中实现输入防抖：添加 `DebounceAndTranslateAsync` 方法，`OnSourceTextChanged` 中取消前一次 CTS 并启动 400ms 延迟。验收：编译通过，快速连续输入只触发一次 pipeline 调用。
- [x] 1.2 添加 `IClipboardService?` 构造函数参数（两个重载均添加）。添加 `CopyTranslationCommand` 和 `ShowCopiedFeedback` 属性。验收：编译通过，复制后 ShowCopiedFeedback 闪现 800ms。
- [x] 1.3 将 `_sourceLanguage` 从 `readonly` 改为可变。添加 `SelectedSourceLanguage` 属性和 `SwapLanguagesCommand`。验收：编译通过，交换源/目标语言后 re-translate。
- [x] 1.4 添加 `SourceTextLength` 属性，在 `OnSourceTextChanged` 中更新。验收：编译通过，字符计数随输入实时变化。
- [x] 1.5 添加 `IsTranslating` 属性，在 `RunPipelineAsync` 开始时设 true，finally 中设 false。验收：编译通过，pipeline 运行中 IsTranslating 为 true。
- [x] 1.6 `StatusText` 在翻译完成后只显示耗时信息（移除快捷键提示），`UpdateModeDisplay` 只更新 ModeLabel。验收：编译通过，StatusText 不含 "Ctrl+Enter"。

## 2. XAML 布局重构

- [x] 2.1 窗口移除固定 `Height="220"`，添加 `SizeToContent="Height"` + `MaxHeight="500"`。源输入 MaxHeight 改为 120，翻译区域 MaxHeight 改为 200。验收：窗口高度自适应内容。
- [x] 2.2 添加 `ProgressBar IsIndeterminate="True"` 绑定 `IsTranslating`，置于翻译区域上方。验收：翻译中显示蓝色进度条。
- [x] 2.3 翻译结果区域内添加 Copy 按钮（右上角），绑定 `CopyTranslationCommand`，文字根据 `ShowCopiedFeedback` 切换。验收：点击 Copy 后短暂显示 "Copied!"。
- [x] 2.4 ComboBox 宽度从 120 缩至 70，ItemTemplate 改为只显示语言代码。添加 "→" 前缀标签。验收：语言选择器紧凑显示。
- [x] 2.5 底栏添加键盘快捷键标签（`Border.kbd` 样式 + monospace 字体），独立于 StatusText。验收：`[Ctrl+Enter]` 和 `[Esc]` 标签始终可见。
- [x] 2.6 底栏添加 ⇄ 互换按钮，绑定 `SwapLanguagesCommand`。添加源语言指示器显示 `SelectedSourceLanguage.Code` 或 fallback "auto"。验收：互换按钮可见且功能正常。
- [x] 2.7 源输入框右下角叠加字符计数显示，绑定 `SourceTextLength`。验收：字符数实时更新。
- [x] 2.8 根 Panel 添加 `Name="RootPanel"` + `Opacity="0"` + `DoubleTransition` (150ms)。验收：窗口有淡入效果。

## 3. Code-behind 与定位

- [x] 3.1 `OnOpened` 中 30ms 延迟设置 RootPanel.Opacity=1 触发淡入。验收：窗口打开有淡入动画。
- [x] 3.2 添加 `FadeOutAndClose` 方法：设 Opacity=0，160ms 后 Close()。Cancel 和关闭按钮改为调用 FadeOutAndClose。验收：窗口关闭有淡出动画。
- [x] 3.3 `App.axaml.cs` 中 `PositionOverlay` 使用估算高度 260px 替代 `overlay.Height`。验收：编译通过，初始定位合理。
- [x] 3.4 新增 `ClampToScreen` 静态方法，使用 `Screens.ScreenFromWindow` 获取工作区并 clamp 位置。在 `ShowOverlay` 中 Show 后 50ms 调用。验收：窗口不超出屏幕可见区域。
- [x] 3.5 `ShowOverlay` 中注入 `IClipboardService`（从 `platform.Clipboard`）到 OverlayViewModel 构造函数。验收：编译通过，复制功能可用。

## 4. 测试

- [x] 4.1 更新现有 OverlayViewModel 测试的 `Task.Delay` 值以适配 400ms 防抖（从 200-300ms 增加到 550-700ms）。验收：所有原有测试通过。
- [x] 4.2 添加防抖测试：快速连续输入只触发一次 pipeline 调用。验收：测试通过。
- [x] 4.3 添加 Copy 测试：CopyTranslationCommand 调用 clipboard、ShowCopiedFeedback 闪现、无 clipboard 时 no-op、空文本时 no-op。验收：4 个测试通过。
- [x] 4.4 添加 IsTranslating 测试：pipeline 运行中为 true、完成后为 false、错误后为 false、清空后为 false。验收：3 个测试通过。
- [x] 4.5 添加 SwapLanguages 测试：交换源目标语言、无 SelectedTarget 时 no-op。验收：2 个测试通过。
- [x] 4.6 添加 SourceTextLength 测试：ASCII 字符计数、多字节字符计数。验收：2 个测试通过。
- [x] 4.7 添加 SelectedSourceLanguage 测试：Settings 构造函数设置、空 source 时为 null。验收：2 个测试通过。

## 5. 集成验证

- [x] 5.1 运行 `dotnet build` 确保全部编译通过。验收：0 error, 0 warning（除已知 MacHotkeyService CS0067）。
- [x] 5.2 运行 `dotnet test` 确保全部测试通过。验收：所有测试绿色。
