# LGTV Switcher 開発計画（Windows/Mac 両対応, .NET 10）

本ドキュメントは、Windows / macOS 両対応の .NET アプリケーションとして、

> **U2725QE の接続状態（自動切替）に連動して  
> LG 49NANO86JNA の入力切替を行う常駐ソフトウェア**

を開発するための設計・実証計画である。

## 🎯 ゴール

- .NET **Generic Host (.NET 10 / net10.0)** を基盤とした構成で実装する
- **Windows を優先して完成**させ、その後 macOS 実装を追加できる構造にする
- OS 差分は **ディスプレイ検知層（DisplayDetection）とデーモンホスト（Daemon）に閉じ込める**
- LG TV webOS 向けのクライアントは OS 非依存に実装し、両プラットフォームで再利用する

---

# 0. 技術方針

- ターゲットフレームワーク
  - 共通ライブラリ: `net10.0`
  - Windows 固有ライブラリ／アプリ: `net10.0-windows`
  - （将来）macOS 向けは `net10.0` でビルドしつつ、P/Invoke やネイティブバインディングで対応
- ホスティングモデル
  - `.NET Generic Host` + `DI` + `IHostedService` / `BackgroundService`
  - Windows: `UseWindowsService()` でサービス化
  - macOS: `launchd`（LaunchDaemon）から起動されるコンソールアプリ
- 内部連携
  - `IDisplayChangeDetector` → `DisplaySyncWorker` → `ILgTvController`
  - インターフェース＋イベントによる **バケツリレー構造**
  - プラットフォーム差分は `IDisplayChangeDetector` の実装に閉じ込める

---

# 1. システム構成の最終イメージ

```text
┌──────────────────────────────┐
│         UI層（任意: Tray / MenuBar）        │
│  - 状態表示 / 手動リトライ / 一時停止など     │
└──────────────┬─────────────┘
               │ IPC or 設定ファイル
┌──────────────▼──────────────┐
│  バックグラウンドサービス本体 (.NET Generic Host) │
│                                              │
│  - IDisplayChangeDetector (Win/Mac実装差し替え) │
│  - ILgTvController (webOS API)               │
│  - DisplaySyncWorker（ディスプレイ→TV切替）     │
│  - 設定 (IOptions<LgTvSwitcherOptions>)       │
│  - ロギング (Microsoft.Extensions.Logging)   │
└──────────────┬──────────────┘
        OSごとの実装
┌──────────────▼──────────────┐
│ Windows:                                      │
│  - HiddenWindow + Win32 + WMI                 │
│  - Windows Service (自動起動, ログオン前も動作)│
└──────────────────────────────┘
┌──────────────────────────────┐
│ macOS:                                      │
│  - CGDisplayReconfigurationCallback         │
│  - LaunchDaemon (ログイン前から動作)        │
└──────────────────────────────┘
````

---

# 2. ソリューション／パッケージ構成

```text
LGTVSwitcher.sln
  ├─ src/
  │   ├─ LGTVSwitcher.Core/                    # OS非依存のドメイン & ワーカー
  │   ├─ LGTVSwitcher.LgWebOsClient/           # LG webOS クライアント
  │   ├─ LGTVSwitcher.DisplayDetection.Windows/# Windows固有のディスプレイ検知
  │   ├─ LGTVSwitcher.Daemon.Windows/          # Windowsサービス / コンソール
  │   ├─ LGTVSwitcher.DisplayDetection.Mac/    # （将来追加）macOS向け検知
  │   └─ LGTVSwitcher.Daemon.Mac/              # （将来追加）macOS Daemon
  │
  ├─ tools/
  │   └─ LGTVSwitcher.Sandbox.Windows/         # 実験用・手動検証用コンソール
  │
  └─ tests/
      ├─ LGTVSwitcher.Core.Tests/
      ├─ LGTVSwitcher.DisplayDetection.Windows.Tests/
      └─ LGTVSwitcher.LgWebOsClient.Tests/
```

## 2-1. LGTVSwitcher.Core（net10.0）

**役割**
OS 非依存のドメインモデルとビジネスロジックを集約する。

**主な内容**

* DTO / Enum

  * `MonitorSnapshot`
  * `MonitorBounds`
  * `MonitorConnectionKind`
  * `DisplaySnapshotChangedEventArgs`
* 抽象

  * `IDisplayChangeDetector`
  * `ILgTvController`
* ワーカー

  * `DisplaySyncWorker`

    * `IDisplayChangeDetector.DisplayChanged` を購読
    * U2725QE の接続状態と LG TV の入力状態を同期する
* 設定

  * `LgTvSwitcherOptions`

    * 対象モニタ識別子（FriendlyName / DeviceName / パターン）
    * LG TV ホスト／ポート／client-key
    * 遅延、リトライポリシーなど

## 2-2. LGTVSwitcher.LgWebOsClient（net10.0）

**役割**
LG webOS の WebSocket API をラップし、`ILgTvController` を実装するクライアント層。

**主な内容**

* 抽象

  * `ILgWebOsClient`（低レベル：接続・送信・受信）
  * `ILgWebSocketTransport`（モックしやすいトランスポート層）
* 実装

  * `LgWebOsClient`（WebSocket での接続・ペアリング・リクエスト実装）
  * `LgTvController`（`ILgTvController` 実装）

    * 入力切替
    * 必要であれば電源オン／アプリ起動など
* テスト前提

  * トランスポート層を差し替え可能にし、JSON 送受信のシナリオを UT で検証可能にする

## 2-3. LGTVSwitcher.DisplayDetection.Windows（net10.0-windows）

**役割**
Windows 固有の Display 検知をここに閉じ込める。

**主な内容**（現在の Experimental コードを移植・整理）

* `WindowsMessagePump`

  * Hidden Window + メッセージループ
  * `WM_DISPLAYCHANGE` / `WM_DEVICECHANGE` を捕捉
* `Win32MonitorEnumerator`

  * `EnumDisplayDevices` / `EnumDisplaySettings` を用いて現在のモニタ構成を列挙
  * WMI (`WmiMonitorID`, `WmiMonitorConnectionParams`) による

    * EDID FriendlyName 解決
    * `VideoOutputTechnology` からの接続種別マッピング
  * `MapVideoOutputTechnology(uint)` で `MonitorConnectionKind` に変換
* `WindowsMonitorDetector : IDisplayChangeDetector`

  * `WindowsMessagePump.WindowMessageReceived` を購読
  * イベント発火時に `Win32MonitorEnumerator` でスナップショットを取得
  * 変化がある場合のみ `DisplayChanged` を発火

## 2-4. LGTVSwitcher.Daemon.Windows（net10.0-windows）

**役割**
Windows Service / コンソールアプリとして実際に動作するホスト。

**主な内容**

* `Program.cs`

  * `Host.CreateDefaultBuilder(args)`
  * `.UseWindowsService()` + `.UseConsoleLifetime()`
  * `.ConfigureServices(...)` で依存登録
* DI 構成例

```csharp
services.Configure<LgTvSwitcherOptions>(configuration.GetSection("LgTvSwitcher"));

services.AddSingleton<IDisplayChangeDetector, WindowsMonitorDetector>();
services.AddSingleton<ILgTvController, LgTvController>(); // LgWebOsClient を内部で利用
services.AddHostedService<DisplaySyncWorker>();
```

* 設定

  * `appsettings.json` と `C:\ProgramData\LGTVSwitcher\` などの固定パスを組み合わせる
* ロギング

  * 最初は `Microsoft.Extensions.Logging` のコンソール／イベントログ
  * 余裕があれば Serilog でファイル出力を追加

## 2-5. LGTVSwitcher.Sandbox.Windows（net10.0-windows）

**役割**
今の Experimental に相当する「Windows 向け検証用コンソール」。

* DisplayDetection の挙動確認
* 実機モニタ構成の JSON ダンプ
* 接続種別（HDMI / DisplayPort / USB）の確認

**中身**

* Generic Host を立て、`DisplayChangeLoggingService` のような Logging 専用 HostedService を登録
* プロダクションの Daemon に手を入れずに、DisplayDetection レイヤーの開発・調査に使う

## 2-6. macOS 向けパッケージ（将来）

Windows が安定した後で次を追加する。

* `LGTVSwitcher.DisplayDetection.Mac`（net10.0）

  * `IDisplayChangeDetector` の macOS 実装
  * `CGDisplayRegisterReconfigurationCallback` をラップし、`MonitorSnapshot` に変換
* `LGTVSwitcher.Daemon.Mac`（net10.0）

  * Generic Host を起動
  * `launchd` 用の wrapper として動作

Core / LgWebOsClient / DisplaySyncWorker は既存のものをそのまま再利用する。

---

# 3. 機能別の実証ステップ

ここからは「何をどの順で作るか」を整理する。

## 3-1. ディスプレイの増減イベント検知（Windows 優先）

1. **Windows の挙動把握**

   * `WM_DISPLAYCHANGE` / `WM_DEVICECHANGE` の発火タイミングを Sandbox でログ観察
   * USB-C ドック／直接接続／RDP など、典型構成での挙動差分を記録
2. **DisplayDetection.Windows の確立**

   * 今の Experimental コードを `LGTVSwitcher.DisplayDetection.Windows` に移植
   * `MonitorConnectionKind` が HDMI / DisplayPort / USB を正しく識別できることを確認
3. **Core 連携**

   * `IDisplayChangeDetector` を Core に移し、Windows 実装を依存として差し込む

## 3-2. LG TV（webOS）入力切替の実証

1. **WebOS API 調査**

   * `lgtv2` 相当の WebSocket API を実機で確認
   * `client-key` の取得／保存方法を決定
2. **LgWebOsClient 実装**

   * `ILgWebSocketTransport` を抽象化しつつ WebSocket クライアントを実装
   * `ILgTvController` 経由で

     * 入力切替
     * 必要なら電源オン
3. **DisplaySyncWorker 連携**

   * `DisplaySyncWorker` で

     * 「U2725QE が online になったら LGTV を PC入力に」
     * 「U2725QE が消えたら TV を別入力に」などのルールを実装・検証

## 3-3. ログイン前から動作する特権サービス化（Windows）

1. **Windows Service 化**

   * `LGTVSwitcher.Daemon.Windows` に `UseWindowsService()` を追加
   * PowerShell スクリプトで `sc.exe create` or `New-Service` を用意
2. **設定・ログ**

   * 設定: `C:\ProgramData\LGTVSwitcher\config.json` + `appsettings.json`
   * ログ: EventLog + ファイル（必要なら Serilog でローテーション）
3. **運用シナリオ**

   * 再起動後も自動スタートすること
   * ログオン前にディスプレイイベントが動作していることを確認

## 3-4. macOS 版の検討と実装

（Windows 版が安定してから着手）

1. `LGTVSwitcher.DisplayDetection.Mac`

   * `CGDisplayRegisterReconfigurationCallback` でディスプレイの変更イベントを検出
   * `MonitorSnapshot` へのマッピングルールを整理
2. `LGTVSwitcher.Daemon.Mac`

   * Generic Host 起動
   * `/Library/LaunchDaemons/com.lgtv.switcher.plist` を使い、ログイン前から起動
3. Windows 版と同じ `LgTvSwitcherOptions` / `DisplaySyncWorker` / `LgWebOsClient` を使って動作確認

---

# 4. テスト戦略

## 4-1. Core テスト（LGTVSwitcher.Core.Tests）

* `DisplaySyncWorker`

  * 擬似的な `IDisplayChangeDetector` 実装（Fake）でイベントを発火させ、
    `ILgTvController`（Mock）への呼び出しが期待どおりかを検証
* 判定ロジック

  * 例えば「U2725QE が HDMI/DP いずれかで online のときだけ切り替える」などのルールの UT

## 4-2. Windows DisplayDetection テスト

* 純粋関数を主にテストする：

  * `MapVideoOutputTechnology(uint)` → `MonitorConnectionKind`
  * `ExtractVendorToken` / `ExtractInstanceToken`
  * EDID マッチング `TryMatchEdidName` のテーブル駆動テスト
* WMI 実クエリや Win32 呼び出し自体は、
  `LGTVSwitcher.Sandbox.Windows` を使った**手動検証**でカバー

## 4-3. LgWebOsClient テスト

* `ILgWebSocketTransport` をモックし、以下を検証：

  * ペアリング時の JSON 要求フォーマット
  * 入力切替コマンドの JSON フォーマット
  * エラー時のリトライや例外マッピング

---

# 5. フェーズ別ロードマップ（Windows → Mac）

1. **Phase 1: Windows DisplayDetection のライブラリ化**

   * Experimental から `Core` / `DisplayDetection.Windows` / `Sandbox.Windows` へ分離
   * HDMI / DP / USB の判定とスナップショット差分検出が安定することを確認
2. **Phase 2: LG WebOS クライアント & DisplaySyncWorker**

   * `LgWebOsClient` 実装
   * `DisplaySyncWorker` で U2725QE ↔ LG TV の同期を実現
   * Windows Service として常駐運用できる状態にする
3. **Phase 3: macOS 版の追加**

   * `DisplayDetection.Mac` / `Daemon.Mac` を追加
   * Windows と同じオプション・ワーカー・クライアントで動作することを確認
4. **Phase 4: ドキュメント & CI 整備**

   * `/docs` にセットアップ手順、トラブルシューティングを整理
   * CI では `dotnet format` / `dotnet build` / 単体テストを実行

---

以上をベースに、当面は **Windows 用のライブラリ分割（Core / DisplayDetection.Windows / Daemon / Sandbox）** から着手する。
そのうえで、DisplaySyncWorker と LgWebOsClient を実装し、「U2725QE を抜き差ししたら LGTV の入力が勝手に切り替わる」ところまでを最初のマイルストーンとする。
