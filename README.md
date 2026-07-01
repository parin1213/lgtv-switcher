# LGTVSwitcher

指定したPCモニターの接続状態を監視し、LG TV (webOS) の入力を自動的に切り替えるための .NET 10 バックグラウンド サービス（デーモン）です。

## 🎯 解決する課題

PCとMacで1台の高性能モニター（例：Dell U2725QE）を共有している環境を想定しています。
モニター側は入力（USB-C/DisplayPort）を自動で切り替えますが、そのPCに接続されたセカンドディスプレイとしてのLG TVは、PCが非アクティブになっても入力が切り替わりません。

このツールは、**プライマリモニターのWindowsへの接続/切断を検知**し、LG TVの入力を自動でPC用（`TargetInputId`）に切り替えたり、あるいは切断時に別の入力（`FallbackInputId`）に戻したりします。加えて、起動時と指定間隔でも最新状態を再評価します。

## 🚀 主な機能

  * **ディスプレイ状態の監視:** 特定のモニター（`PreferredMonitorName`で指定）がPCに接続されているかをバックグラウンドで監視します。
  * **LG TV (webOS) との連携:** モニターの接続/切断イベントに加え、起動時と指定間隔でも再評価し、指定した入力に自動で切り替えます。
  * **条件付き切り替え:** 現在の入力が指定の一覧に含まれるときだけ切り替えます（`AllowedCurrentInputIds`）。
  * **自動ペアリング:** 実行時にLG TVとのペアリングを試み、自動で保存します。
  * **自動再接続:** TVとのWebSocket接続がアイドル状態などで切断されても、次の操作が必要になったタイミングで自動的に再接続を試みます。
  * **タスクスケジューラ対応:** ログオン時にバックグラウンドで自動起動するよう構成可能です。

## 🔧 設定方法

インストール先、またはビルドディレクトリにある `appsettings.json` を編集します。

```json
{
  "LgTvSwitcher": {
    // LG TVのホスト名（mDNS）またはIPアドレス
    "TvHost": "lgwebostv.local",
    // TVのポート番号（通常 3000 または 3001） 
    // ※最近LG TV WebOSでは 'wss' かつ 3001ポートでないと受付しない為、大抵の場合は3001にしてください。
    "TvPort": 3001,
    
    // 監視対象モニターがPCに接続されたときに切り替えたいTV側の入力ID
    "TargetInputId": "HDMI_4",
    
    // 監視対象モニターがPCから切断されたときに切り替えたい入力ID
    // (空文字列の場合は何もしません)
    "FallbackInputId": "", 

    // LG TVの現在入力がこの一覧に含まれるときだけ切り替える
    // (空配列/未指定なら常に切り替え対象)
    "AllowedCurrentInputIds": [ "HDMI_2" ],

    // ディスプレイ変化がなくても再同期する間隔（秒）
    // (0以下は無効)
    "SyncIntervalSeconds": 0,
    
    // 監視対象とするモニターの「フレンドリーネーム」
    // "DELL U2725QE" など。部分一致で判定されます。
    // この名前が空だと、TVの切り替えは実行されません。
    "PreferredMonitorName": "",

    // 優先モニタが「このPCを映している」とみなす映像入力インターフェース名の一覧。
    // 使える名前: "DisplayPort"("DP") / "HDMI" / "USB-C"("USB"/"Thunderbolt") / "DVI" / "VGA"。
    // 別モニタで値が異なる場合は生の値 "0x1B" / "27" も指定可。
    // 既定は ["DisplayPort"]。空配列にすると DDC 判定を使わず従来の列挙ベースにフォールバック。
    "PreferredMonitorThisPcInputSources": [ "DisplayPort" ]
  }
}
```

### なぜ DDC/CI で入力ソースを見るのか

Dell U2725QE のような USB-C/KVM ハブ搭載モニタは、**表示ソースを別PC(Mac の USB-C 等)へ切り替えても、Windows への DisplayPort リンクを生かしたまま**にします。そのため Windows の列挙(GDI/WMI)ではモニタが常に「接続中」に見え、「今このPCを映しているか」を判別できません（このツールが TV を勝手に奪ってしまう原因でした）。

そこで **DDC/CI（VCP 0x60 = Input Source）でモニタ本体に「今どの入力を映しているか」を直接問い合わせ**、優先モニタが `PreferredMonitorThisPcInputSources` の入力（既定は DisplayPort）を映しているときだけ TV を `TargetInputId` に切り替えます。別ソース(Mac)を映している間は TV に一切触れません。DDC は稀に読み取りに失敗するため、短時間リトライと直近確定値の保持で判定を安定させています。

対応値の確認は次のコマンドで行えます（優先モニタの現在入力ソースを表示）:

```powershell
dotnet run --project src/LGTVSwitcher.Daemon.Windows -- probe-input
```

**`PreferredMonitorName` の確認方法:**
PowerShellで以下のコマンドを実行すると、現在接続されているモニターの情報がコンソールに出力されます。その文字列をコピーして設定してください。

例:

```powershell
PS > Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorID | Where-Object { $_.Active } | ForEach-Object { -join ($_.UserFriendlyName | Where-Object { $_ -ne 0 } | ForEach-Object { [char]$_ }) }
LG TV SSCR2
DELL U2725QE
```

## 📦 インストールと常用 (Daemon)

常用する場合は `LGTVSwitcher.Daemon.Windows` を使用します。付属のスクリプトでタスクスケジューラ（ログオン時実行）に登録できます。

### 前提条件

  * .NET 10 Runtime がインストールされていること（自己完結発行する場合は不要ですが、スクリプトのデフォルト設定に依存します）

### 手順

1.  PowerShellを**管理者権限**で開きます。
2.  `scripts` フォルダ内のインストールスクリプトを実行します。

```powershell
# プロジェクトのルートディレクトリにて
gsudo .\scripts\sudo-install.bat
```

これにより、アプリケーションがビルドされ、`C:\Tools\LGTVSwitcher` に配置され、現在のユーザーのログオン時に自動起動するタスクとして登録されます。

### アンインストール

```powershell
gsudo .\scripts\sudo-uninstall.bat
```

## 🧪 動作確認・デバッグ

設定の確認や、モニター認識の挙動をテストしたい場合は `LGTVSwitcher.Daemon.Windows` をコンソールアプリとして実行します。

```bash
# ビルド
dotnet build

# 実行
cd src/LGTVSwitcher.Daemon.Windows\bin\Debug\net10.0-windows
.\LGTVSwitcher.Daemon.Windows.exe
```

初回実行時、LG TV側にペアリングの許可を求めるプロンプトが表示されるので、リモコンで「許可」を選択してください。

### 📝ログの保管場所

アプリケーション構成によって出力場所が異なります

* プロダクション構成
  * `%LOCALAPPDATA%\LGTVSwitcher\logs\log-*.txt`
* デバッグ構成
  * `.\logs\log-*.txt`


