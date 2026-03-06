## Why

Nuke Build 脚手架已创建但 Target 实现为空壳，没有实际构建逻辑。产品需要完整的 CI/CD 流水线：Clean → Restore → Build → Test（含覆盖率）→ Publish（跨平台安装包）→ 自动更新支持。当前手动 `dotnet publish` 无法满足分发需求。

## What Changes

- 补全 Nuke Build Target 实现：Clean、Restore、Build、Test（含覆盖率报告）
- 新增 Publish Target：为 Windows 和 macOS 生成自包含安装包
- 集成 Velopack 实现应用自动更新（delta 增量更新、后台检查、用户提示安装）
- 在 LiveLingo.App 中集成更新检查逻辑（启动时 + 定时）

## Capabilities

### New Capabilities
- `nuke-build-targets`: 补全 Nuke Build 的 Clean/Restore/Build/Test Target 实现，连接 Solution 和 runsettings
- `nuke-publish`: Nuke Publish Target，使用 `dotnet publish` 生成 win-x64 / osx-arm64 自包含包，再通过 Velopack 打包为安装包和更新包
- `auto-update`: 应用内自动更新：Velopack 集成、启动检查、后台定时检查、用户提示、增量下载安装

### Modified Capabilities

## Impact

- `build/BuildTask.cs`：从空壳变为完整流水线
- `build/_build.csproj`：新增 Velopack 工具依赖
- `src/LiveLingo.App/LiveLingo.App.csproj`：新增 Velopack NuGet 包
- `src/LiveLingo.App/App.axaml.cs` 或新增 `Services/UpdateService.cs`：更新检查逻辑
- 新增 `publish/` 输出目录（gitignore）
