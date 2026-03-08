## Why

当前 Release workflow 是单一 Job 在 Windows runner 上一步完成编译+测试+打包+发布，缺乏阶段隔离。macOS 安装包完全缺失，Windows 只有 Velopack Setup.exe 没有 MSI，releases/ 目录中还会输出 nupkg、delta、json 等非用户文件。此外 macOS 运行时 Dock 栏仍显示应用图标（应仅常驻菜单栏）。

## What Changes

- 重构 GitHub Actions release.yml：拆分为 Compile → Test → Pack (Win + Mac matrix) → Release 四个独立 Job
- Windows 发布物增加 MSI 安装包（exe + MSI 并行提供）
- macOS 发布物增加 .pkg 安装包（在 macos-latest runner 上构建）
- 过滤 release artifacts，排除源码、json、nupkg、delta 等非安装包文件
- 修复 macOS 下 Dock 栏出现应用图标的问题，确保仅常驻菜单栏

## Capabilities

### New Capabilities
- `release-pipeline`: GitHub Actions release workflow 清晰化四阶段（Compile → Test → Pack → Release），Win + Mac 矩阵构建，发布物过滤
- `mac-dock-fix`: 修复 macOS Dock 图标问题，确保应用仅以 menu-bar agent 模式运行

### Modified Capabilities
- `nuke-publish`: 增加 Windows MSI 打包 Target，优化 release 目录仅保留最终安装包

## Impact

- `.github/workflows/release.yml`：从单 Job 重构为多 Job 流水线
- `build/BuildTask.cs`：新增 MSI 打包 Target，调整 Pack 输出过滤
- `build/_build.csproj`：可能新增 WiX 或 dotnet-msi 工具依赖
- `src/LiveLingo.Desktop/Program.cs`：修复 `SetMacAgentMode` 时机或实现
- `src/LiveLingo.Desktop/Platform/macOS/MacNativeMethods.cs`：可能调整 P/Invoke 签名
