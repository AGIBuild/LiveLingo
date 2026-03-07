## Context

LiveLingo 已完成 P1-P5 功能开发，具备 Windows 端完整的翻译 + AI 后处理 + 设置管理能力。当前使用 Nuke Build 脚手架（`build/BuildTask.cs`），但所有 Target 都是空壳。需要：

1. 补全构建流水线，使 `nuke` 一条命令走完全流程
2. 生成跨平台安装包（Windows `.exe` 安装器 / macOS `.dmg`）
3. 支持应用内自动更新（增量更新、后台检查）

现有项目结构：
- `LiveLingo.slnx`：Solution（src/ + tests/）
- `build/BuildTask.cs`：Nuke NukeBuild 子类，空壳 Target
- `build/_build.csproj`：Nuke 构建项目，.NET 10，引用 `Nuke.Common 10.1.0`
- `test.runsettings`：覆盖率配置
- `src/LiveLingo.App/`：Avalonia 桌面应用（WinExe），支持 win-x64 / osx-arm64

## Goals / Non-Goals

**Goals:**
- 补全 Nuke Target：Clean → Restore → Compile → Test（含覆盖率）→ Publish → Pack
- Publish 生成 `dotnet publish` 自包含输出
- Pack 使用 Velopack `vpk` 打包为安装器 + 增量更新包
- 应用内嵌入 Velopack `UpdateManager` 实现自动更新（启动检查 + 定时检查）
- 支持 Windows（win-x64）和 macOS（osx-arm64）双平台

**Non-Goals:**
- Linux 支持
- CI/CD 平台配置（GitHub Actions / Azure DevOps）—— 不在此变更范围
- 代码签名证书管理
- 自建更新服务器 —— 使用 GitHub Releases 或本地文件源

## Decisions

### D1: 自动更新框架选择 → Velopack

| 方案 | 优点 | 缺点 |
|---|---|---|
| Velopack | 活跃维护，跨平台（Win/Mac/Linux），增量更新，MIT，Avalonia 官方示例 | 相对较新 |
| Squirrel.Windows | 成熟 | 仅 Windows，维护停滞 |
| ClickOnce | 微软内置 | 不支持 macOS，UI 不可控 |

**选择 Velopack**：跨平台、增量更新、与 Avalonia 集成良好。

### D2: Velopack 集成点

```
Program.Main()
  └─ VelopackApp.Build().Run()   ← 最早位置，处理安装/卸载/更新钩子
  └─ BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)
```

更新检查在 `App.OnFrameworkInitializationCompleted` 中通过 `IUpdateService` 发起。

### D3: UpdateService 设计

```csharp
public interface IUpdateService
{
    Task<bool> CheckForUpdateAsync(CancellationToken ct = default);
    Task DownloadAndApplyAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    string? AvailableVersion { get; }
    bool IsUpdateAvailable { get; }
}
```

实现类 `VelopackUpdateService` 封装 `UpdateManager`。DI 注册为 Singleton。

在 `App.OnFrameworkInitializationCompleted` 中启动时检查一次，之后每 4 小时后台定时检查。

### D4: Nuke Target 依赖图

```
Clean ──→ Restore ──→ Compile ──→ Test ──→ Publish ──→ Pack
                                    │
                                    └─ 生成覆盖率报告
```

- `Clean`：删除 `src/**/bin`, `src/**/obj`, `tests/**/bin`, `tests/**/obj`, `publish/`, `releases/`
- `Restore`：`dotnet restore LiveLingo.slnx`
- `Compile`：`dotnet build LiveLingo.slnx --no-restore`
- `Test`：`dotnet test LiveLingo.slnx --no-build --settings test.runsettings --collect:"XPlat Code Coverage"` + ReportGenerator
- `Publish`：`dotnet publish src/LiveLingo.App -c Release -r {runtime} --self-contained -o publish/{runtime}`
- `Pack`：`vpk pack --packId LiveLingo --packVersion {version} --packDir publish/{runtime} --mainExe LiveLingo.App.exe`

### D5: 版本管理

使用 Nuke `[Parameter]` 接收版本号，默认从 `LiveLingo.App.csproj` 的 `<Version>` 读取。

```csharp
[Parameter("Version for packaging (semver2)")]
readonly string Version;
```

### D6: 运行时标识

```csharp
[Parameter("Target runtime (win-x64, osx-arm64)")]
readonly string Runtime = "win-x64";
```

### D7: 输出目录约定

```
publish/{runtime}/      ← dotnet publish 输出
releases/               ← vpk pack 生成的安装器 + nupkg + delta
```

两个目录加入 `.gitignore`。

## Risks / Trade-offs

- **[Risk] Velopack 版本兼容性**：.NET 10 Preview 可能有兼容性问题 → 锁定 Velopack 最新稳定版，CI 中验证
- **[Risk] macOS 签名**：未签名的 macOS 应用可能被 Gatekeeper 阻止 → Non-Goal，后续迭代处理
- **[Risk] 更新检查网络失败**：离线或网络不可达时更新检查应静默失败，不影响正常使用
- **[Trade-off] 自包含发布体积较大**（~100MB+）：换来不依赖用户系统的 .NET Runtime 安装
