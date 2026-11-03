# Windows 打包与更新设计（2026-11-03）

## 问题

- Windows 发布了两种安装方式：Velopack `Setup.exe`（能自动更新）和自维护的 WiX `.msi`（不能）。用 `.msi` 的用户永远看不到更新，
  界面还把"没有检查"说成"已是最新"。同一件事（安装与更新）由两套机制实现，违反"同一功能只由一个组件实现"。
- Windows 上用户数据（设置、模型、日志）放在 `%LocalAppData%\LiveLingo`，与 Velopack 的安装根目录（`packId` = `LiveLingo`）是同一个目录。

## 事实（已核对）

- [VERIFIED] Velopack 1.2.158 起支持 `vpk pack --msi`：MSI 与 Setup.exe 安装同样的目录结构，装完后更新方式一致
  （docs.velopack.io/packaging/installer）。本仓库用 vpk 1.2.161。
- [VERIFIED] Velopack 卸载会删除整个 `%LocalAppData%\{packId}`（docs.velopack.io/integrating/uninstalling）。
- [VERIFIED] Velopack 安装器遇到非空目标目录时，用户确认后会把旧目录改名、重装、成功后**删除旧目录**
  （velopack `src/bins/src/commands/install.rs`）。所以数据留在安装根目录里，首次安装和卸载都会丢模型与设置。
- [VERIFIED] 更新只替换 `current` 目录，根目录其他内容不动。

## 决策

| # | 决策 | 备选项 | 取舍 |
|---|------|--------|------|
| 1 | Windows 只用 Velopack 一条打包链：`Setup.exe`（个人）+ `.msi`（`vpk pack --msi`，面向部署工具） | 自维护 WiX MSI；MSIX；ClickOnce；只留 Setup.exe | 自维护 MSI 不能自更新，且要维护第二套工程和 WiX 许可（v7 的 OSMF）。MSIX 未签名无法安装，要 Store 或证书；ClickOnce 无增量更新、不跨平台。Velopack 同时覆盖 Windows 与 macOS，有增量更新。MSI 保留是因为已有用户使用且部署工具需要 |
| 2 | MSI 固定 `--instLocation PerUser` | `PerMachine` / `Either` | 与 Setup.exe 一致：后台更新不需要提权。按机器安装的更新是否弹 UAC 未验证，不冒险 |
| 3 | 数据目录移出安装根目录：Windows `%LocalAppData%\AGIBuild\LiveLingo`，macOS 不变；由 `LiveLingo.Core.UserData` 一处定义 | 数据留在原处；改 `packId` | 留在原处会在安装和卸载时被清空。改 `packId` 会切断已安装版本的更新链，macOS 也受影响。卸载后保留数据与 macOS 行为一致 |
| 4 | 首次启动一次性把 `settings.json`、`preferences.json`、`models` 从旧目录改名搬走；失败只记日志，不阻断启动 | 复制；不迁移 | 同一磁盘卷内改名，GB 级模型瞬间完成。不迁移会让已装 Setup.exe 的用户丢设置、重新下载模型 |

## 已知限制与未决项

- 旧 `.msi`（2.1.0 及以前）的用户在装新版前，必须自己把旧目录里的三项搬到新目录，否则安装器替换旧目录时会删掉它们
  （旧版没有代码可以替他们搬）。已写入 CHANGELOG。
- 未签名：SmartScreen 会警告首次安装。微软推荐非 Store 分发使用 Azure Artifact Signing（约 10 美元/月，需组织或个人身份验证，
  信誉随发布逐步积累）。Velopack 支持 `--signTemplate` / Azure Trusted Signing。需要账号和密钥，不在本次范围。
- `winget` 分发（`winget upgrade` 也能升级）：需要先有签名与稳定的下载地址，之后再评估。
- `.msi` 的产物文件名、`--msi` 在 CI 上的输出需要在首次 Windows 发布时核对（本地 macOS 无法运行 MSI 打包）。
