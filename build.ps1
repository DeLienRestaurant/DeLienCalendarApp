# DeLien Calendar for Windows 配布物ビルドスクリプト
#   .\desktop\build.ps1
# 出力: desktop\dist\DeLienCalendar-win-x64.zip
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\DeLienCalendar.Desktop\DeLienCalendar.Desktop.csproj'
$dist = Join-Path $root 'dist'
$appDir = Join-Path $dist 'app'
$zipPath = Join-Path $dist 'DeLienCalendar-win-x64.zip'

if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

Write-Host "ビルドしています..." -ForegroundColor Cyan
dotnet publish $project -c $Configuration -r win-x64 --self-contained false -o $appDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish に失敗しました。"
}

Copy-Item (Join-Path $root 'install.ps1') $appDir -Force
Copy-Item (Join-Path $root 'README.md') $appDir -Force

Write-Host "ZIP を作成しています..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $appDir '*') -DestinationPath $zipPath -Force

Write-Host "完了: $zipPath" -ForegroundColor Green
