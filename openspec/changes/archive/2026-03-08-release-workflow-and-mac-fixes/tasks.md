## 1. macOS Dock 图标修复

- [x] 1.1 移除 `Program.cs` 中 `SetMacAgentMode()` 方法及其调用，该调用在 Avalonia 初始化前执行会被覆盖
- [x] 1.2 在 `App.OnFrameworkInitializationCompleted` 中（Avalonia 初始化后）调用 `setActivationPolicy:` 设置为 `NSApplicationActivationPolicyAccessory`（值 1）
- [x] 1.3 验证 `build/macos/Info.plist` 保留 `LSUIElement = true`（已有，确认未被删除）

## 2. Windows MSI 打包

- [x] 2.1 安装 WiX v5 CLI 工具，添加到 `.config/dotnet-tools.json`（`dotnet tool install wix`）
- [x] 2.2 创建 `build/windows/LiveLingo.wxs` WiX 源文件，定义 MSI 包结构（安装目录、文件收集、快捷方式）
- [x] 2.3 在 `BuildTask.cs` 中新增 `PackMsi` Target：调用 `wix build` 生成 `releases/LiveLingo-{version}-win-x64.msi`，依赖 `Publish`
- [x] 2.4 调整 `Pack` Target，在 vpk pack 后将 Setup.exe 重命名为 `LiveLingo-{version}-win-x64-Setup.exe`

## 3. macOS 打包优化

- [x] 3.1 调整 `PackMac` Target 输出命名，确保 .pkg 文件名为 `LiveLingo-{version}-osx-arm64.pkg`（已符合，确认）

## 4. Release Workflow 重构

- [x] 4.1 重构 `.github/workflows/release.yml`：拆分为 compile / test / pack / release 四个 Job
- [x] 4.2 compile Job：在 `ubuntu-latest` 上执行 `dotnet build` 验证编译通过
- [x] 4.3 test Job：依赖 compile，在 `ubuntu-latest` 上执行 Nuke Test（单元测试 + 覆盖率）
- [x] 4.4 pack Job：依赖 test，使用 matrix 策略 `[{os: windows-latest, runtime: win-x64}, {os: macos-latest, runtime: osx-arm64}]`
- [x] 4.5 pack/windows 步骤：执行 `nuke Pack PackMsi`，上传 `*.exe` + `*.msi` 为 workflow artifacts
- [x] 4.6 pack/macos 步骤：执行 `nuke PackMac`，上传 `*.pkg` 为 workflow artifact
- [x] 4.7 release Job：依赖 pack，下载所有 artifacts，仅上传 `*.exe` `*.msi` `*.pkg` 到 GitHub Release
- [x] 4.8 确保 Release 不包含源码归档、json、nupkg、delta、RELEASES 等非安装包文件
