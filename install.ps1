# DeLien Calendar for Windows インストールスクリプト（管理者権限不要）
#   powershell -ExecutionPolicy Bypass -File .\install.ps1
# インストール先: %LOCALAPPDATA%\Programs\DeLienCalendar
param(
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

$source = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $env:LOCALAPPDATA 'Programs\DeLienCalendar'
$exe = Join-Path $dest 'DeLienCalendar.exe'

Write-Host "インストール先: $dest" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $dest | Out-Null

# 実行中のアプリがあれば停止する（更新時）
$running = Get-Process -Name 'DeLienCalendar' -ErrorAction SilentlyContinue
if ($running) {
    $running | ForEach-Object { Stop-Process -Id $_.Id -Force }
    Start-Sleep -Milliseconds 500
}

Get-ChildItem -Path $source -File | Where-Object { $_.Name -ne 'install.ps1' } | ForEach-Object {
    Copy-Item $_.FullName -Destination $dest -Force
}
Get-ChildItem -Path $source -Directory | ForEach-Object {
    Copy-Item $_.FullName -Destination $dest -Recurse -Force
}

# スタートメニューのショートカット
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcutPath = Join-Path $startMenu 'DeLien Calendar.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $dest
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = 'DeLien Calendar'
$shortcut.Save()

Write-Host "インストールが完了しました。" -ForegroundColor Green

if (-not $NoLaunch) {
    Start-Process -FilePath $exe
}
