# DeskFlow を単一ファイルの DeskFlow.exe として publish し、%LOCALAPPDATA%\DeskFlow に配置してショートカットを作成します。
#   powershell -ExecutionPolicy Bypass -File scripts\install.ps1 [-Run] [-NoShortcut]
param([switch]$NoShortcut, [switch]$Run)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destDir = Join-Path $env:LOCALAPPDATA 'DeskFlow'
$exe = Join-Path $destDir 'DeskFlow.exe'

# 起動中の DeskFlow を終了（exe 版 / dll 版の両方）
Get-Process DeskFlow -ErrorAction SilentlyContinue | Stop-Process -Force
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
  Where-Object { $_.CommandLine -like '*DeskFlow.dll*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Start-Sleep -Milliseconds 500

dotnet publish (Join-Path $root 'DeskFlow.csproj') -p:PublishProfile=SingleFile --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'publish に失敗しました' }

New-Item -ItemType Directory -Force $destDir | Out-Null
Copy-Item (Join-Path $root 'publish\DeskFlow.exe') $exe -Force

# 旧バージョン（dotnet DeskFlow.dll 方式）の残りを削除
$old = Join-Path $destDir 'app'
if (Test-Path $old) { Remove-Item -Recurse -Force $old }

if (-not $NoShortcut) {
  $shell = New-Object -ComObject WScript.Shell
  foreach ($dir in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
    $lnk = $shell.CreateShortcut((Join-Path $dir 'DeskFlow.lnk'))
    $lnk.TargetPath = $exe
    $lnk.Arguments = ''
    $lnk.WorkingDirectory = $destDir
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = 'DeskFlow - デスクトップ動画プレーヤー'
    $lnk.Save()
  }
  Write-Output 'デスクトップとスタートメニューに DeskFlow のショートカットを作成しました'
}

# 自動起動が登録済みならパスを更新
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty $runKey -Name DeskFlow -ErrorAction SilentlyContinue) {
  Set-ItemProperty $runKey -Name DeskFlow -Value "`"$exe`""
}

Write-Output "インストール先: $exe"
if ($Run) { Start-Process $exe -WorkingDirectory $destDir }
