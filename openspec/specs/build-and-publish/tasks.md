## 1. Nuke Build Targets 补全

- [x] 1.1 在 `BuildTask.cs` 中添加 `[Solution("LiveLingo.slnx")]` 字段，定义 `RootDirectory` / `PublishDir` / `ReleasesDir` 路径常量
- [x] 1.2 实现 `Clean` Target：删除 `src/**/bin`、`src/**/obj`、`tests/**/bin`、`tests/**/obj`、`publish/`、`releases/`
- [x] 1.3 实现 `Restore` Target：调用 `dotnet restore` 解析 Solution
- [x] 1.4 重命名 `Build` → `Compile` Target：调用 `dotnet build --no-restore` 使用 `Configuration` 参数
- [x] 1.5 实现 `Test` Target：调用 `dotnet test --no-build --settings test.runsettings --collect:"XPlat Code Coverage"`，依赖 `Compile`
- [x] 1.6 验证：运行 `nuke Test` 全流程通过

## 2. Nuke Publish & Pack

- [x] 2.1 添加 `[Parameter] Runtime` 和 `[Parameter] Version` 参数
- [x] 2.2 实现 `Publish` Target：调用 `dotnet publish src/LiveLingo.App -c Release -r {Runtime} --self-contained -o publish/{Runtime}`，依赖 `Test`
- [x] 2.3 安装 Velopack CLI 工具（dotnet tool），在 `_build.csproj` 中配置 `vpk` 可用性
- [x] 2.4 实现 `Pack` Target：调用 `vpk pack` 生成安装包和更新包到 `releases/`，依赖 `Publish`
- [x] 2.5 在 `.gitignore` 中添加 `publish/` 和 `releases/` 条目（已有）
- [x] 2.6 验证：运行 `nuke Pack --runtime win-x64 --version 0.1.0`，确认 `releases/` 下生成安装器

## 3. 自动更新集成

- [x] 3.1 在 `LiveLingo.App.csproj` 中添加 Velopack NuGet 包引用
- [x] 3.2 在 `Program.cs` 的 `Main()` 中添加 `VelopackApp.Build().Run()` 初始化
- [x] 3.3 创建 `IUpdateService` 接口（`CheckForUpdateAsync`、`DownloadAndApplyAsync`、`IsUpdateAvailable`、`AvailableVersion`）
- [x] 3.4 实现 `VelopackUpdateService`：封装 `UpdateManager`，实现检查/下载/应用逻辑
- [x] 3.5 在 DI 容器中注册 `IUpdateService` 为 Singleton
- [x] 3.6 在 `App.OnFrameworkInitializationCompleted` 中发起启动时更新检查（后台、静默失败）
- [x] 3.7 实现定时更新检查（默认 4 小时间隔，可通过 `UserSettings` 配置）
- [x] 3.8 在 `UserSettings` 中添加 `UpdateUrl` 和 `UpdateCheckIntervalHours` 配置项
- [x] 3.9 验证：构建通过，`IUpdateService` 单元测试覆盖检查/下载/失败路径
