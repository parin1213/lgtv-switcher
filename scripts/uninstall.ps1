<# 
 .SYNOPSIS
   LGTVSwitcher のタスク設定を削除し、インストール先を片付けるスクリプト。

 .NOTES
   管理者権限で実行してください。
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Admin {
    $currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($currentIdentity)
    if (-not $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "管理者権限で実行してください。"
    }
}

try {
    Assert-Admin

    $appName = "LGTVSwitcher.Daemon.Windows"
    $serviceName = "LGTVSwitcher"
    $taskName = "LGTVSwitcherTask"
    $installDir = "C:\Tools\LGTVSwitcher"

    # 1. タスクの停止と削除
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Write-Host "タスクを停止・削除しています..." -ForegroundColor Cyan
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    } else {
        Write-Host "タスクは見つかりませんでした。" -ForegroundColor Gray
    }

    # 2. 旧サービスの削除（念のため）
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        Write-Host "旧Windowsサービスを削除しています..." -ForegroundColor Cyan
        sc.exe stop $serviceName | Out-Null 2>$null
        sc.exe delete $serviceName | Out-Null 2>$null
    }

    # 3. プロセスの強制終了
    $runningProcess = Get-Process -Name $appName -ErrorAction SilentlyContinue
    if ($runningProcess) {
        Write-Host "実行中のプロセスを終了しています..." -ForegroundColor Cyan
        Stop-Process -Name $appName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }

    # 4. ファイル削除
    if (Test-Path $installDir) {
        $response = Read-Host "インストール先 $installDir を削除しますか？ (y/N)"
        if ($response -match '^(y|Y)$') {
            Remove-Item -Recurse -Force $installDir
            Write-Host "削除しました: $installDir" -ForegroundColor Green
        } else {
            Write-Host "インストール先はそのまま残します。" -ForegroundColor Yellow
        }
    }

    Write-Host "アンインストール完了" -ForegroundColor Green
}
catch {
    Write-Error $_
    exit 1
}