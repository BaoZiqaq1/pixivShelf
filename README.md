# pixivShelf \- 第三方 pixiv 下载工具

# pixivShelf

\<p align="center"\>

\<img src="assets/banner\.png" alt="pixivShelf Banner" width="100%"\>

\</p\>

\<p align="center"\>

第三方 pixiv 下载工具

\</p\>

\<p align="center"\>

批量下载、灵活整理，将喜欢的作品保存到本地。

\</p\>

## 特色功能

- 一次性下载账号收藏的所有图片

- 根据需求自由调整是否下载成人内容，避免尴尬

- 将收藏时间写入文件信息

- 并发下载，支持暂停与断点续传

## 项目信息

- 项目地址：[pixivShelf](https://github.com/BaoZiqaq1/pixivShelf)

- 基于项目：[PixivFetcher](https://github.com/Kyy008/PixivFetcher)

- 作者：smile丶luo \& DeepSeek

## 安装

### 安装版（推荐）

双击 `pixivShelfSetup-1.0.0.exe`。

- 默认安装到 `%LOCALAPPDATA%\Programs\pixivShelf`，无需管理员权限，安装时可自选目录。

- 可勾选创建桌面快捷方式和开始菜单文件夹。

- 卸载方式：

    - 开始菜单「卸载 pixivShelf」

    - 或 设置 → 应用 → pixivShelf

- 卸载界面有复选框「同时删除配置、登录状态和任务记录」，默认不勾选；不勾选只删除程序，勾选后才会连同 `data\` 一起删除。

- 下载的图片不会被卸载程序删除。

### 便携版

解压 `PixivShelf-win-x64.zip`，双击 `PixivShelf.exe` 即可使用，无需安装。想彻底清理时，删除整个文件夹即可。

### 首次启动

无论使用哪种方式，首次启动都会进入四步引导：

1. 免责声明

2. 登录

3. 下载偏好

4. 开始

### 运行环境

|组件|版本|
|---|---|
|\.NET 桌面运行时|8\.0\.x|
|Windows App Runtime|2\.6|

## 数据存储

程序默认将数据保存在程序目录的 `data\` 文件夹：

|文件|用途|
|---|---|
|`config.json`|设置及 Cookie|
|`tasks.db`|下载任务|
|`WebView2\`|登录缓存|
|`logs\`|启动日志|

- 整个程序文件夹可以直接迁移。

- 删除 `data\` 即可重置程序。

- 程序目录不可写时，自动使用 `%LOCALAPPDATA%\PixivShelf\`。

- 旧版本根目录下的配置和数据库会在首次启动时自动迁移。

## 从源码构建

需要 \.NET 8 SDK。

```PowerShell
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
# 编译
& $dotnet build PixivShelf.sln -c Debug
# 运行测试
& $dotnet test tests\PixivShelf.Tests\PixivShelf.Tests.csproj
# 发布并生成 ZIP
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish.ps1 -Zip
# 生成安装包（需要先安装 Inno Setup 6）
winget install JRSoftware.InnoSetup
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

## 常见问题

### 登录失败？

优先使用内置浏览器登录。Google 账号登录受阻时，可尝试手动粘贴 Cookie。

### 下载速度慢？

检查网络代理，或适当调整设置中的并发数。

### 程序无法启动？

确认已安装所需运行时。仍无法启动时，设置 `PIXIV_TRACE=1` 后重新运行，查看 `data\logs\startup.log`。

### 如何初始化？

删除 `data` 文件夹。

## 许可证

本项目使用 **MIT** 许可证，详见 \[LICENSE\]\(LICENSE\)。

本项目基于 [PixivFetcher](https://github.com/Kyy008/PixivFetcher) 重构，原项目同样使用 MIT 许可证（© 2026 Kyy008），其版权声明已保留在 \[LICENSE\]\(LICENSE\) 中。

## 免责声明

pixivShelf 是独立的第三方开源工具，与 pixiv 官方无任何关联。

请尊重作品版权，仅在合理范围内下载和使用作品，并合理控制下载频率。所有作品版权归原作者所有。
