# pixivShelf

<p align="center">
  <img src="assets/banner.png" alt="pixivShelf Banner" width="100%">
</p>

<p align="center">
  将喜欢的作品保存到本地
</p>


## 特色功能

- 打包下载账号收藏的所有图片
- 根据需求自由调整是否下载成人内容，避免尴尬
- 将收藏时间写入文件信息
- 并发下载，支持暂停与断点续传

## 项目信息

- 地址 https://github.com/BaoZiqaq1/pixivShelf
- 作者：smile丶luo & DeepSeek
- 基于 PixivFetcher https://github.com/Kyy008/PixivFetcher 重构优化。


## 快速开始

### 安装包

前往 Releases 下载 `pixivShelfSetup-1.0.0.exe`，双击打开根据提示操作即可。

- 卸载方式：
  - 开始菜单「卸载 pixivShelf」
  - 设置 → 应用 → pixivShelf
  - 下载的图片不会被删除

### 便携版

前往 Releases 下载 `PixivShelf-win-x64.zip`，解压后双击 `PixivShelf.exe` 即可使用。

- 卸载方式：
  - 删除整个文件夹。


### 运行环境

| 组件 | 版本 |
|---|---|
| .NET 桌面运行时 | 8.0.x |
| Windows App Runtime | 2.6 |

## 数据存储

默认将数据保存在程序目录的 `data\` 文件夹：

| 文件 | 用途 |
|---|---|
| `config.json` | 设置及 Cookie |
| `tasks.db` | 下载任务 |
| `WebView2\` | 登录缓存 |
| `logs\` | 启动日志 |

- 整个程序文件夹可以直接迁移。
- 删除 `data\` 即可清除数据。
- 程序目录不可写时，自动使用 `%LOCALAPPDATA%\PixivShelf\`。

## 从源码构建

需要 .NET 8 SDK。

```powershell
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

### 登录失败
优先使用网页登录。登录受阻时，可尝试手动粘贴 Cookie。

### 下载速度慢
检查网络代理，或适当调整设置中的并发数。

### 无法启动
确认已安装所需运行环境，仍无法启动时，设置 `PIXIV_TRACE=1` 后重新运行，查看 `data\logs\startup.log`。

### 清除数据
删除 `data` 文件夹。

## 许可证
本项目使用 **MIT** 许可证，详见 [LICENSE](LICENSE)。

pixivShelf 是独立的第三方开源工具，与 pixiv 官方无任何关联。
