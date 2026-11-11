# AGENTS.md

给 AI 助手 / 维护者的项目说明。面向人的文档在 `README.md`，这里只放**改代码时需要知道的事**。

## 项目概况

- 第三方 pixiv 下载器，C# + WinUI 3（Windows App SDK 2.6.5），未打包（unpackaged）。
- 解决方案 `PixivShelf.sln`：Core / Cli / WinUI / Tests 四个项目，66 项单元测试必须保持绿。
- 版本 `1.0.0`，来自 `src/PixivShelf.WinUI/PixivShelf.WinUI.csproj` 的 `<Version>`。
- 界面全中文，没有本地化资源，文案直接写在 XAML / C# 里。

## 目录

```
src/PixivShelf.Core/        与 UI 无关的逻辑（网络、下载、存储、命名、过滤）
src/PixivShelf.Cli/         命令行工具，AssemblyName = pixivfetch-cli
src/PixivShelf.WinUI/       WinUI 3 界面
  Pages/                    DownloadPage / TasksPage / SettingsPage / AboutPage
  Services/                 AppState / Motion / PageSizing / StartupTrace
tests/PixivShelf.Tests/     xUnit
installer/                  Inno Setup 脚本 + 带复选框的卸载入口 + 中文语言包
scripts/publish.ps1         发布到 dist\PixivShelf（-Zip 同时生成压缩包）
scripts/build-installer.ps1 生成 dist\pixivShelfSetup-<version>.exe
docs/screenshots/           界面截图（README / Release 用）
docs/portable-readme.txt   便携版说明，publish.ps1 会复制进发布包
tools/icon/                 图标源文件（source.webp / app.ico / 生成脚本）
```

## 构建 / 测试 / 发布

```powershell
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"

& $dotnet build PixivShelf.sln -c Debug
& $dotnet test tests\PixivShelf.Tests\PixivShelf.Tests.csproj

# 发布（必须走脚本：它清理 pdb / 个人数据 / 补 README.txt）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish.ps1 -Zip

# 安装包（需要 Inno Setup 6）
winget install JRSoftware.InnoSetup
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

注意：

- 本机 PowerShell 执行策略禁止直接 `& .\scripts\xxx.ps1`，一律用 `powershell -NoProfile -ExecutionPolicy Bypass -File`。
- 发布是 `--self-contained false` + `WindowsAppSDKSelfContained=false`，目标机器需装 .NET 8 桌面运行时和 Windows App Runtime 2.6。
- 免安装版：发布加 `--self-contained true`，并把 `WindowsAppSDKSelfContained` 改 `true`（约 164MB）。

## 安装包机制

| 文件 | 作用 |
|---|---|
| `installer/pixivShelf.iss` | Inno 脚本：中文向导、自选目录、桌面/开始菜单任务、LZMA2 压缩 |
| `installer/uninstall.cmd` | 卸载入口，转调 `uninstall.ps1` |
| `installer/uninstall.ps1` | PowerShell + WinForms 对话框：复选框默认不勾，勾了才删 `data\` |
| `installer/languages/ChineseSimplified.isl` | 中文语言包（非官方，需 UTF-8 BOM） |

- **Inno 的卸载脚本不支持自定义窗口**（`CreateCustomForm` 在 uninstall 阶段不可用），所以卸载 UI 用 PowerShell 做，`uninstall.cmd` 由开始菜单快捷方式调用。
- 安装最后一步 `DeinitializeSetup` 把注册表 `UninstallString` 覆盖成 `uninstall.cmd`，让「设置 → 应用」也走带复选框的入口；`QuietUninstallString` 保持 `unins000.exe /VERYSILENT`（静默卸载保留 data）。
- 载荷来自 `dist\PixivShelf`，`[Files]` 排除 `data\*`、`*.pdb`、`config.json`、`tasks.db`、`*.WebView2\*`。
- `.iss` 和 `.ps1` 必须保存为 **UTF-8 with BOM**，否则中文在安装界面/对话框里会乱码。

## Core 模块职责

| 文件 | 职责 |
|---|---|
| `PixivClient.cs` | 所有 HTTP：收藏/画师/作品抓取、图片下载、cookie 校验、重试与退避 |
| `DownloadEngine.cs` | 收集 → 生产者/消费者（`Channel<T>` + N 个 worker）→ 下载图片，统计跳过/失败 |
| `TaskStore.cs` | SQLite 任务库（单连接 + `lock (_gate)`，WAL），记录任务与每作品完成状态 |
| `FilterPolicy.cs` | 唯一过滤入口 `Evaluate()`，返回 `(保留?, 原因)` |
| `FileNamePolicy.cs` | 按模板生成路径；`%page_number%` 与 `%page_index%` 都从 0 起算 |
| `FileNamePresets.cs` | 8 套中文命名预设 + `BuildSample()`，设置页和引导页共用 |
| `BookmarkTime.cs` / `FileTimes.cs` | 收藏时间推算、NTFS 时间写入 |
| `PasteParser.cs` | 从粘贴文本解析 PID / 画师 ID |
| `Backfill.cs` | 「整理已有文件」：回填时间 / 改名 |
| `TransferMeter.cs` | 全局字节计数，供状态栏算下载速度 |
| `Settings.cs` | 配置模型、原子保存、默认目录与旧数据迁移 |

## WinUI 结构

| 文件 | 职责 |
|---|---|
| `App.xaml.cs` | 启动：未完成引导则开 `OnboardingWindow`，否则直接 `MainWindow` |
| `OnboardingWindow` | 4 步引导，独立 `AppSettings`，最后一步才写配置 |
| `MainWindow` | 导航壳（下载/任务/设置/关于）+ 底部状态栏 + 速度采样 |
| `LoginWindow` | WebView2 内嵌登录，读取 cookie（含 HttpOnly） |
| `Services/AppState.cs` | 单例：设置、任务库、任务集合、运行中任务、日志、Toast |
| `Services/Motion.cs` | 动效助手（淡入、位移、高度展开、按压），尊重系统「减少动画」 |
| `Services/PageSizing.cs` | 页面内容宽度/居中 |

## 必须遵守的约束（踩过的坑）

1. **发布要带 `*.pri` 和 `*.xbf`**：`dotnet publish` 不会自动复制，缺了启动即崩（`Microsoft.UI.Xaml.dll` + `0x80070002`）。csproj 的 `IncludeWinUIResourcesInPublish` 负责补齐，别删。
2. **`Microsoft.Data.Sqlite` 锁死 `8.0.33`**：浮动版本会导致「编译用新版、发布带旧版」，报 `Could not load assembly`。
3. **配置必须原子写**：`Settings.Save()` 写 `.tmp` 再 `File.Replace`；解析失败时坏文件备份为 `config.json.bad`。
4. **SQLite 只在 `TaskStore` 的锁里访问**：多 worker 会并发回调 `onArtworkCompleted`。
5. **`%page_number%` 是 p0 起算**，`%page_index%` 是 `0` 起算；改动会改变已有用户的文件名。
6. **收藏可见性**：`rest=show` / `rest=hide`。匿名访问也会下发 `PHPSESSID`，**不能**用它判断登录，必须 `ResolveSelfUserIdAsync`（读 `x-userid`）。
7. **文案两处同步**：文件时间开关同时出现在 `OnboardingWindow.xaml` 和 `Pages/SettingsPage.xaml`。
8. **引导完成标记**：`config.json` 的 `OnboardingCompleted`；老配置没有该字段时必须视为已完成。
9. **默认下载目录**：`图片\PixivShelf`（`Settings.Normalize()` 补默认值）。
10. **品牌**：显示名 `pixivShelf`，exe / 程序集 `PixivShelf`，数据目录 `data\`。

## 测试钩子（环境变量）

| 变量 | 用途 |
|---|---|
| `PIXIV_UI_START=tasks\|settings\|about` | 启动后直接打开指定页 |
| `PIXIV_UI_AUTOSTART=bookmarks\|pid:123\|artist:456` | 启动后自动开始任务 |
| `PIXIV_TRACE=1` | 写 `data\logs\startup.log`，排查启动崩溃 |

## 改完代码的检查清单

1. `dotnet build` 0 警告 0 错误
2. `dotnet test` 66 项全过
3. `scripts\publish.ps1 -Zip` 成功
4. 启动 `dist\PixivShelf\PixivShelf.exe` 冒烟：引导 / 主界面 / 关于页不崩
5. 改了安装相关脚本时：`scripts\build-installer.ps1` 成功，并验证安装→卸载（默认保留 data、勾选后删除 data）
6. 发布包里不能出现 `config.json`、`tasks.db`、`*.pdb`、`PixivShelf.exe.WebView2`
