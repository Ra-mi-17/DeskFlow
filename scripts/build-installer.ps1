# 単一ファイルの DeskFlow.exe を publish し、インストーラー（publish\installer\DeskFlow-Setup-<version>.exe）を作ります。
#   powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
# 必要: .NET 8 SDK、Inno Setup 6（winget install JRSoftware.InnoSetup）
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'DeskFlow.csproj'

$version = ([xml](Get-Content $proj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'DeskFlow.csproj の <Version> が見つかりません' }

$iscc = @(
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 が見つかりません（winget install JRSoftware.InnoSetup）' }

$publish = Join-Path $root 'publish'
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
dotnet publish $proj -p:PublishProfile=SingleFile --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'publish に失敗しました' }

& $iscc /Q "/DAppVersion=$version" (Join-Path $root 'installer\DeskFlow.iss')
if ($LASTEXITCODE -ne 0) { throw 'インストーラーの作成に失敗しました' }

$setup = Join-Path $publish "installer\DeskFlow-Setup-$version.exe"
Write-Output "作成しました: $setup"
