param(
    [string]$Configuration = "Release",
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (!(Test-Path $dotnet)) { $dotnet = "dotnet" }

$output = Join-Path $root "dist\PixivShelf"
if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

& $dotnet publish (Join-Path $root "src\PixivShelf.WinUI\PixivShelf.WinUI.csproj") `
    -c $Configuration -p:Platform=x64 -r win-x64 --self-contained false -o $output --nologo -v m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Never ship local secrets or runtime cache in a release folder.
Get-ChildItem $output -Filter "*.pdb" -File -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
foreach ($name in @("config.json", "tasks.db", "tasks.db-wal", "tasks.db-shm", "startup.log")) {
    $path = Join-Path $output $name
    if (Test-Path $path) { Remove-Item $path -Force }
}
$webViewCache = Join-Path $output "PixivShelf.exe.WebView2"
if (Test-Path $webViewCache) { Remove-Item $webViewCache -Recurse -Force }

# Keep only the explanatory README inside data\.
$data = Join-Path $output "data"
if (Test-Path $data) {
    Get-ChildItem $data -Force | Where-Object { $_.Name -ne "README.txt" } | Remove-Item -Recurse -Force
}

Copy-Item (Join-Path $root "docs\portable-readme.txt") (Join-Path $output "README.txt") -Force

if ($Zip) {
    $zipPath = Join-Path $root "dist\PixivShelf-win-x64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zipPath
}

Write-Host "Release folder: $output"
if ($Zip) { Write-Host "Release zip:    $zipPath" }
