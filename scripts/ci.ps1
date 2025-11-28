<# 
 .SYNOPSIS
   ローカルまたはCIで実行するビルド・テストスクリプト。

 .NOTES
   .NET SDK がインストールされている前提。
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    Write-Host "dotnet restore..." -ForegroundColor Cyan
    dotnet restore

    Write-Host "dotnet build (Release)..." -ForegroundColor Cyan
    dotnet build lgtv-switcher.slnx -c Release

    Write-Host "dotnet test (Release)..." -ForegroundColor Cyan
    dotnet test lgtv-switcher.slnx -c Release

    Write-Host "完了: build/test OK" -ForegroundColor Green
}
catch {
    Write-Error $_
    exit 1
}
