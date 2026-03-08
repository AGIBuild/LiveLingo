## Context

当前状态：
- `release.yml` 单 Job 在 `windows-latest` 运行 `./build.ps1 Pack --Version xxx`，所有阶段混在一起
- macOS 安装包 (`.pkg`) 的 Nuke Target (`PackMac`) 已实现，但 release workflow 未调用（只有 Windows runner）
- Windows 只通过 Velopack 生成 `Setup.exe`，缺少 MSI
- `releases/` 目录包含 `.nupkg`、delta、`RELEASES` 等 Velopack 内部文件，全部上传到 GitHub Release
- `Program.cs` 中 `SetMacAgentMode()` 在 Avalonia 初始化前调用 `setActivationPolicy:`，但 Avalonia 的 `StartWithClassicDesktopLifetime` 会重置为 Regular 策略

## Goals / Non-Goals

**Goals:**
- 将 release workflow 拆分为 Compile → Test → Pack → Release 四个独立 Job
- Pack Job 使用 matrix 策略同时构建 Win + Mac 安装包
- Windows 提供 exe 安装包（Velopack Setup）和 MSI 安装包
- macOS 提供 .pkg 安装包
- Release Job 仅上传最终安装包文件，排除源码、json、nupkg、delta 等
- 修复 macOS Dock 图标问题，确保仅以 Accessory 模式运行

**Non-Goals:**
- Linux 平台支持
- 代码签名 / 公证 (Notarization)
- CI workflow (push/PR) 的修改
- Velopack 自动更新机制的变更

## Decisions

### D1: Release Workflow Job 拆分

```
compile (ubuntu-latest)
    ↓
  test (ubuntu-latest)
    ↓
  pack (matrix: [win-x64, osx-arm64])
  ├── windows-latest: Pack + PackMsi
  └── macos-latest: PackMac
    ↓
  release (ubuntu-latest, needs: pack)
  └── Download all artifacts → gh-release
```

| 方案 | 优点 | 缺点 |
|---|---|---|
| 单 Job（现状） | 简单 | 无法跨平台，阶段不清晰 |
| 4 Job 流水线 | 阶段隔离，矩阵构建，失败定位精准 | 每个 Job 需重新 checkout + restore |

**选择 4 Job 流水线**：清晰的阶段隔离 + 天然支持矩阵跨平台构建。

### D2: MSI 打包方案 → WiX v5

| 方案 | 优点 | 缺点 |
|---|---|---|
| WiX Toolset v5 | 行业标准，.NET CLI 工具 (`wix`)，MIT 许可 | 需要 `.wxs` 配置文件 |
| Inno Setup | 简单配置 | 只生成 exe，不支持 MSI |
| dotnet-msi | 轻量 | 不够成熟，功能有限 |

**选择 WiX v5**：
- 通过 `dotnet tool` 安装：`dotnet tool install --global wix`
- 创建 `build/windows/LiveLingo.wxs` 描述 MSI 结构
- Nuke 新增 `PackMsi` Target 调用 `wix build`
- 输出 `releases/LiveLingo-{version}-win-x64.msi`

### D3: Release Artifact 过滤策略

当前 `releases/` 目录 Velopack 输出包含：
- `LiveLingo-Setup.exe` ✅ 需要
- `LiveLingo-{ver}-full.nupkg` ❌ 排除
- `LiveLingo-{ver}-delta.nupkg` ❌ 排除
- `RELEASES` ❌ 排除

策略：在 Release Job 中通过 glob 仅匹配 `*.exe`、`*.msi`、`*.pkg` 文件上传。

### D4: macOS Dock 图标修复

**根因分析**：`Program.cs` 中 `SetMacAgentMode()` 在 `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` 之前调用，但 Avalonia 初始化时会重新创建 NSApplication 并重置 activation policy 为 Regular。

**修复方案**：将 `setActivationPolicy:` 调用移至 `App.OnFrameworkInitializationCompleted` 中（Avalonia 初始化完成后），确保不被覆盖。

```
Before:  Program.Main → SetMacAgentMode → Avalonia.Start (overrides!)
After:   Program.Main → Avalonia.Start → OnFrameworkInitializationCompleted → SetMacAgentMode ✓
```

| 方案 | 优点 | 缺点 |
|---|---|---|
| 移到 OnFrameworkInitializationCompleted | 时机正确，Avalonia 不会再覆盖 | 需要从 Program.cs 移动代码到 App |
| Info.plist LSUIElement（现有） | 标准 macOS 方式 | 仅对 .app bundle 生效，开发时直接运行无效 |
| 两者结合 | .app bundle + 直接运行都生效 | 无 |

**选择两者结合**：保留 `Info.plist` 中 `LSUIElement = true`（生产环境 .app bundle），同时在 `OnFrameworkInitializationCompleted` 中程序化设置（开发和直接运行场景）。

### D5: Pack Job Matrix 策略

```yaml
strategy:
  matrix:
    include:
      - os: windows-latest
        runtime: win-x64
        targets: Pack PackMsi
      - os: macos-latest
        runtime: osx-arm64
        targets: PackMac
```

每个 matrix 项上传对应平台的安装包 artifacts，Release Job 汇总下载后发布。

## Risks / Trade-offs

- **[Risk] WiX v5 在 CI 中的安装**：需要在 GitHub Actions Windows runner 上 `dotnet tool install wix` → 用 dotnet tool manifest 管理
- **[Risk] macOS runner 时间成本**：macos-latest runner 计费为 Linux 10倍 → 但仅在 Release 时触发，频率低
- **[Risk] Avalonia 未来版本可能更改 activation policy 行为** → `Info.plist` 作为兜底保障
- **[Trade-off] 每个 Job 需要重新 checkout + restore** → 通过 NuGet cache 缓解，换来的是阶段清晰度和跨平台支持
