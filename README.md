# LGTVSwitcher

指定したPCモニターの接続状態を監視し、LG TV (webOS) の入力を自動的に切り替えるための .NET バックグラウンド サービスです。

## 🎯 解決する課題

PCとMacで1台の高性能モニター（例：Dell U2725QE）を共有している環境を想定しています。
モニター側は入力（USB-C/DisplayPort）を自動で切り替えますが、そのPCに接続されたセカンドディスプレイとしてのLG TVは、PCが非アクティブになっても入力が切り替わりません。

このツールは、**プライマリモニターのWindowsへの接続/切断を検知**し、LG TVの入力を自動でPC用（`TargetInputId`）に切り替えたり、あるいは切断時に別の入力（`FallbackInputId`）に戻したりします。

## 🚀 主な機能

  * **ディスプレイ状態の監視:** WindowsのWin32 APIとWMIを使用し、特定のモニター（`PreferredMonitorName`で指定）がPCに接続されているかをバックグラウンドで監視します。
  * **LG TV (webOS) との連携:** モニターの接続/切断イベントをトリガーに、LG TVのwebOS WebSocket APIを呼び出し、指定した入力に自動で切り替えます。
  * **自動ペアリング:** 実行時にLG TVとのペアリングを試み、取得した`ClientKey`を`appsettings.json`に自動で保存します。
  * **自動再接続:** TVとのWebSocket接続がアイドル状態などで切断されても、次の操作が必要になったタイミングで自動的に再接続を試みます。

## 🔧 設定方法

実行ファイルと同じディレクトリにある `appsettings.json` を編集します。

```json
{
  "LgTvSwitcher": {
    // LG TVのホスト名（mDNS）またはIPアドレス
    "TvHost": "lgwebostv.local",
    // TVのポート番号（通常 3000 または 3001）
    "TvPort": 3001,
    
    // ClientKeyは初回実行時に自動取得・保存されるため、空のままでOKです
    "ClientKey": "",
    
    // 監視対象モニターがPCに接続されたときに切り替えたいTV側の入力ID
    "TargetInputId": "HDMI_4",
    
    // 監視対象モニターがPCから切断されたときに切り替えたい入力ID
    // (空文字列の場合は何もしません)
    "FallbackInputId": "", 
    
    // 監視対象とするモニターの「フレンドリーネーム」
    // "DELL U2725QE" など。部分一致で判定されます。
    // この名前が空だと、TVの切り替えは実行されません。
    "PreferredMonitorName": ""
  }
}
```

**`PreferredMonitorName` の確認方法:**
このアプリ（`LGTVSwitcher.Sandbox.Windows`）を実行すると、現在接続されているモニターの情報がJSON形式でコンソールに出力されます。その `FriendlyName` の値をコピーして設定してください。

例:

```json
  "Monitors": [
    {
      "DeviceName": "\\\\.\\DISPLAY1",
      "FriendlyName": "DELL U2725QE", // <-- この名前を設定する
      "Connection": "DisplayPort"
    },
    // ...
  ]
```

## 🏃 実行方法 (Sandbox)

現在は `LGTVSwitcher.Sandbox.Windows` プロジェクトをコンソールアプリとして実行します。

```bash
# ビルド
dotnet build

# 実行
cd LGTVSwitcher.Sandbox.Windows\bin\Debug\net10.0-windows
.\LGTVSwitcher.Sandbox.Windows.exe
```

初回実行時、LG TV側にペアリングの許可を求めるプロンプトが表示されるので、リモコンで「許可」を選択してください。成功すると `appsettings.json` に `ClientKey` が書き込まれます。
