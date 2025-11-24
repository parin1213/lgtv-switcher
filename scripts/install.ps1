<# 
 .SYNOPSIS
   LGTVSwitcher.Daemon.Windows をビルドし、タスクスケジューラ（ログオン時実行）としてインストールするスクリプト。
   既存のWindowsサービス版があれば削除します。

 .NOTES
   管理者権限で実行してください。
   タスクは「実行ユーザー（$env:USERNAME）」の権限で登録されます。
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
    $serviceName = "LGTVSwitcher"          # 旧サービス名
    $taskName = "LGTVSwitcherTask"         # 新タスク名
    
    $projectPath = Join-Path -Path (Split-Path -Parent $PSScriptRoot) -ChildPath "LGTVSwitcher.Daemon.Windows/LGTVSwitcher.Daemon.Windows.csproj"
    $installDir = "C:\Tools\LGTVSwitcher"
    
    # ---------------------------------------------------------
    # 1. クリーンアップ（旧サービス・旧タスク・実行中プロセスの停止）
    # ---------------------------------------------------------
    Write-Host "クリーンアップを実行中..." -ForegroundColor Cyan

    # 旧Windowsサービスの削除
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        Write-Host "旧Windowsサービスを削除しています..." -ForegroundColor Yellow
        sc.exe stop $serviceName | Out-Null 2>$null
        sc.exe delete $serviceName | Out-Null 2>$null
        Start-Sleep -Seconds 2
    }

    # 既存タスクの削除
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Write-Host "既存のタスクを削除しています..." -ForegroundColor Yellow
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    }

    # 実行中のプロセスを停止 (ファイル上書きのため)
    $runningProcess = Get-Process -Name $appName -ErrorAction SilentlyContinue
    if ($runningProcess) {
        Write-Host "実行中のプロセスを停止しています..." -ForegroundColor Yellow
        Stop-Process -Name $appName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }

    # ---------------------------------------------------------
    # 2. ビルドと配置
    # ---------------------------------------------------------
    if (-not (Test-Path $projectPath)) {
        throw "プロジェクトが見つかりません: $projectPath"
    }

    if (-not (Test-Path $installDir)) {
        New-Item -ItemType Directory -Path $installDir | Out-Null
    }

    Write-Host "dotnet publish 実行中..." -ForegroundColor Cyan
    # コンソール画面を出さない(WinExe)ために PublishSingleFile は便利ですが、必須ではありません。
    # ここでは従来通り単一ファイル発行を指定します。
    $publishCmd = "dotnet publish `"$projectPath`" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true"
    Invoke-Expression $publishCmd

    $publishDir = Join-Path -Path (Split-Path -Parent $projectPath) -ChildPath "bin/Release/net10.0-windows/win-x64/publish"
    
    Write-Host "発行物をコピーしています..." -ForegroundColor Cyan
    Get-ChildItem $publishDir | Copy-Item -Destination $installDir -Recurse -Force

    # ---------------------------------------------------------
    # 3. タスクスケジューラへの登録
    # ---------------------------------------------------------
    Write-Host "タスクスケジューラに登録しています..." -ForegroundColor Cyan
    
    $exePath = Join-Path $installDir "$appName.exe"
    
    # トリガー: ログオン時
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    
    # アクション: 作業ディレクトリを指定しないと appsettings.json を読めないので必須
    $action = New-ScheduledTaskAction -Execute $exePath -WorkingDirectory $installDir
    
    # 設定: 電源接続時のみ等の制限解除、多重起動禁止、強制停止なし(TimeLimit 0)
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Seconds 0) `
        -MultipleInstances IgnoreNew

    # 登録実行 (現在のユーザー権限で実行されるように設定)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -User $env:USERNAME | Out-Null

    # ---------------------------------------------------------
    # 4. 起動確認
    # ---------------------------------------------------------
    Write-Host "タスクを開始します..." -ForegroundColor Cyan
    Start-ScheduledTask -TaskName $taskName

    Write-Host "インストール完了: $taskName" -ForegroundColor Green
    Write-Host "※ 現在のユーザー ($env:USERNAME) のログオン時にバックグラウンドで自動起動します。" -ForegroundColor Gray
}
catch {
    Write-Error $_
    exit 1
}