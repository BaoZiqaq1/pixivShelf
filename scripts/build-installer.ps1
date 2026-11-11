param(
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (!(Test-Path $dotnet)) { $dotnet = "dotnet" }

# --- 版本号（来自 WinUI 项目） ---
$csproj = Join-Path $root "src\PixivShelf.WinUI\PixivShelf.WinUI.csproj"
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (!$version) { $version = "1.0.0" }
Write-Host "版本: $version"

# --- 载荷 ---
if (!$SkipPublish) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts\publish.ps1") -Zip
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$payload = Join-Path $root "dist\PixivShelf"
if (!(Test-Path (Join-Path $payload "PixivShelf.exe"))) {
    throw "找不到载荷目录 $payload，先运行 scripts\publish.ps1"
}

# --- 找 ISCC ---
$candidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$iscc) {
    throw "未找到 ISCC.exe。请先安装 Inno Setup 6：winget install JRSoftware.InnoSetup"
}

Write-Host "ISCC: $iscc"
& $iscc "/DMyAppVersion=$version" (Join-Path $root "installer\pixivShelf.iss")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$setup = Join-Path $root "dist\pixivShelfSetup-$version.exe"
if (Test-Path $setup) {
    $size = [math]::Round((Get-Item $setup).Length / 1MB, 1)
    Write-Host "安装包: $setup ($size MB)"
} else {
    throw "安装包未生成"
}
