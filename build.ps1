# DeLien Calendar for Windows 配布物ビルドスクリプト
#   .\desktop\build.ps1                                   # ZIP のみ
#   .\desktop\build.ps1 -Version 0.2.0 -WithInstaller     # ZIP + インストーラ
#
# 出力:
#   desktop\dist\DeLienCalendar-win-x64.zip          … ポータブル版
#   desktop\dist\DeLienCalendar-<version>-setup.exe  … インストーラ（-WithInstaller 時）
param(
    [string]$Configuration = 'Release',
    [string]$Version = '',
    [switch]$WithInstaller
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $root
$project = Join-Path $root 'src\DeLienCalendar.Desktop\DeLienCalendar.Desktop.csproj'
$dist = Join-Path $root 'dist'
$appDir = Join-Path $dist 'app'
$zipPath = Join-Path $dist 'DeLienCalendar-win-x64.zip'

if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

$publishArgs = @($project, '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'false', '-o', $appDir)
if ($Version -ne '') {
    $publishArgs += "-p:Version=$Version"
}

Write-Host "ビルドしています... (Version=$(if ($Version -ne '') { $Version } else { 'csproj の値' }))" -ForegroundColor Cyan
dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish に失敗しました。'
}

# 配布物に説明・ライセンス・セキュリティポリシーを同梱する
Copy-Item (Join-Path $root 'install.ps1') $appDir -Force
Copy-Item (Join-Path $root 'README.md') $appDir -Force
foreach ($file in @('LICENSE', 'SECURITY.md')) {
    # 本リポジトリ（desktop/ 配下）とアプリ用リポジトリ（ルート配置）の両方に対応する
    $source = @((Join-Path $root $file), (Join-Path $repoRoot $file)) |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    if ($source) {
        Copy-Item $source $appDir -Force
    }
    else {
        Write-Warning "$file が見つかりません。"
    }
}

Write-Host 'ZIP を作成しています...' -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $appDir '*') -DestinationPath $zipPath -Force
Write-Host "ZIP: $zipPath" -ForegroundColor Green

if ($WithInstaller) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) {
        $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
        if ($cmd) { $iscc = $cmd.Source }
    }
    if (-not $iscc) {
        throw 'Inno Setup (ISCC.exe) が見つかりません。https://jrsoftware.org/isdl.php からインストールしてください。'
    }

    $installerVersion = if ($Version -ne '') { $Version } else { '0.0.0' }
    Write-Host 'インストーラを作成しています...' -ForegroundColor Cyan
    & $iscc "/DAppVersion=$installerVersion" (Join-Path $root 'installer\setup.iss')
    if ($LASTEXITCODE -ne 0) {
        throw 'インストーラの作成に失敗しました。'
    }
    Write-Host "インストーラ: $(Join-Path $dist "DeLienCalendar-$installerVersion-setup.exe")" -ForegroundColor Green
}

