# LGTV Switcher 開発計画（Windows/Mac 両対応, .NET 10）

本ドキュメントは、Windows / macOS 両対応の .NET アプリケーションとして、

> **U2725QE などのモニター接続状態（自動切替）に連動して  
> LG TV (webOS) の入力切替を行う常駐ソフトウェア**

を開発するための設計・実証計画である。

## 🎯 ゴール

  - .NET **Generic Host (.NET 10)** を基盤とした構成で実装する
  - **Windows 版 (v1.0)** を完成とし、安定稼働させる **[達成済]**
  - OS 差分は **ディスプレイ検知層（DisplayDetection）とデーモンホスト（Daemon）に閉じ込める**
  - LG TV webOS 向けのクライアントは OS 非依存に実装し、将来の macOS 対応で再利用する

-----

# 0. 技術方針

  - **ターゲットフレームワーク**
      - 共通ライブラリ: `net10.0`
      - Windows 固有: `net10.0-windows`
  - **ホスティングモデル**
      - `.NET Generic Host` + `DI` + `BackgroundService`
      - **Windows:** ユーザーセッションでのディスプレイ検知を確実にするため、**タスクスケジューラ（ログオン時実行）** で起動するコンソールアプリとして構成
      - **macOS:** `launchd`（LaunchAgent）から起動されるコンソールアプリ（予定）
  - **内部アーキテクチャ (Rx)**
      - `IDisplaySnapshotProvider` → `IObservable<DisplaySnapshot>` → `DisplaySyncWorker`
      - 従来のイベント駆動ではなく、**Reactive Extensions (Rx)** を用いたストリーム処理で、短時間の接続フラつき（チャタリング）除去や状態比較を行う
  - **設定と状態の分離**
      - **設定 (`appsettings.json`):** ユーザーが編集する静的な設定（TVのIP、ターゲット入力端子など）
      - **状態 (`device-state.json`):** アプリが管理する動的な情報（ペアリング済み `ClientKey`）。`%LOCALAPPDATA%` に分離して保存する。

-----

# 1. システム構成

```text
┌───────────────────────────────────────────────┐
│  バックグラウンドサービス (.NET Generic Host)       │
│                                               │
│  [DisplaySyncWorker]                          │
│     ▲ (Subscribe)                             │
│     │ Rx Pipeline (Debounce / Distinct)       │
│     │                                         │
│  [IDisplaySnapshotProvider]                   │
│     ▲ (Publish Snapshot)                      │
│     │                                         │
│  [ILgTvController] ──▶ LG TV (WebSocket)      │
│                                               │
└────────┬──────────────────────┬───────────────┘
         │                      │
  OSごとの実装 (Detection)     永続化 (Storage)
┌────────▼──────────────┐  ┌────▼────────────────┐
│ Windows:              │  │ device-state.json   │
│ - WindowsMessagePump  │  │ (ClientKey)         │
│   (Hidden Window)     │  └─────────────────────┘
│ - Win32 API / WMI     │
└───────────────────────┘
┌───────────────────────┐
│ macOS (Planned):      │
│ - CGDisplay...        │
└───────────────────────┘
```

-----

# 2. ソリューション／パッケージ構成

```text
LGTVSwitcher.sln
  ├─ src/
  │   ├─ LGTVSwitcher.Core/                    # [共通] ドメインモデル, Rxワーカー, 設定定義
  │   ├─ LGTVSwitcher.LgWebOsClient/           # [共通] webOS WebSocketクライアント
  │   ├─ LGTVSwitcher.DisplayDetection.Windows/# [Win] Win32/WMIによる検知・Rxプロバイダ
  │   ├─ LGTVSwitcher.Daemon.Windows/          # [Win] 常駐用ホスト (Task Scheduler登録)
  │   │
  │   ├─ LGTVSwitcher.DisplayDetection.Mac/    # [Mac] (未着手) macOS向け検知
  │   └─ LGTVSwitcher.Daemon.Mac/              # [Mac] (未着手) macOS Daemon
  │
  ├─ scripts/                                  # [Win] インストール・アンインストールスクリプト
  │
  └─ tests/
      ├─ LGTVSwitcher.Core.Tests/              # Rxパイプライン・ロジックのテスト
      ├─ LGTVSwitcher.DisplayDetection.Windows.Tests/
      └─ LGTVSwitcher.LgWebOsClient.Tests/
```

## 2-1. LGTVSwitcher.Core (net10.0)

  - **役割:** OS 非依存のビジネスロジック。
  - **実装済み:**
      - `DisplaySnapshot`: モニター構成の不変オブジェクト
      - `DisplaySyncWorker`: Rx を用いたメインループ（Snapshot → フィルタ → TV操作）
      - `LgTvSwitcherOptions`: 設定クラス

## 2-2. LGTVSwitcher.LgWebOsClient (net10.0)

  - **役割:** LG webOS 制御。
  - **実装済み:**
      - `LgTvController`: 接続管理、入力切替、フォアグラウンドアプリ確認
      - `DefaultWebSocketTransport`: 再接続ロジックを含む通信層
      - `LgTvResponseParser`: JSON パース処理

## 2-3. LGTVSwitcher.DisplayDetection.Windows (net10.0-windows)

  - **役割:** Windows 固有のモニタ検知。
  - **実装済み:**
      - `WindowsMessagePump`: Hidden Window で `WM_DISPLAYCHANGE` を受信
      - `Win32MonitorEnumerator`: `EnumDisplayDevices` と WMI で詳細情報を取得
      - `WindowsDisplaySnapshotProvider`: 上記を統合し `IObservable` を提供

## 2-4. LGTVSwitcher.Daemon.Windows (net10.0-windows)

  - **役割:** 実行用アプリケーション。
  - **実装済み:**
      - `FileBasedLgTvClientKeyStore`: `ClientKey` の分離保存
      - Serilog によるファイルログ出力
      - タスクスケジューラへの登録機構

-----

# 3. 開発ステータスとロードマップ

## Phase 1: Windows 検知と Core ロジック [完了]

  - [x] Win32 API (`EnumDisplayDevices`) と WMI によるモニター情報取得
  - [x] Rx (`System.Reactive`) を導入し、イベント乱発を防ぐパイプライン構築
  - [x] `device-state.json` による認証情報の分離保存

## Phase 2: LG TV 連携と常駐化 [完了]

  - [x] webOS WebSocket 接続、ペアリングフローの実装
  - [x] `DisplaySyncWorker` による「切断時・接続時」の入力切替ロジック
  - [x] `install.ps1` によるタスクスケジューラ登録（管理者権限なしでの実行を回避）

## Phase 3: テストと品質向上 [現在進行中]

  - [x] `Core.Tests`: Rx パイプラインの単体テスト（Fakeプロバイダを使用）
  - [ ] `LgWebOsClient.Tests`: 異常系レスポンスのテスト拡充
  - [ ] CI (GitHub Actions) のセットアップ: ビルドとテストの自動化

## Phase 4: macOS 対応 [Planned]

  - [ ] `LGTVSwitcher.DisplayDetection.Mac`
      - `CGDisplayRegisterReconfigurationCallback` の実装
      - `MonitorSnapshot` へのマッピング
  - [ ] `LGTVSwitcher.Daemon.Mac`
      - LaunchAgent (`.plist`) の作成
      - サービスとしてのパッケージング

-----

# 4. テスト戦略

## 4-1. Core テスト

Rx パイプラインのテストを最重要視する。実際の時間は待たず、`TestScheduler` またはモックを用いて、以下のシナリオを検証する。

  - 瞬断（On -\> Off -\> On）が短時間で起きた場合に無視されるか（Debounce）
  - 全く同じ構成の通知が連続した場合に無視されるか（Distinct）
  - 通信エラー発生時にパイプラインが停止せず、リトライされるか

## 4-2. Windows 検出テスト

実機依存が強いため、自動テストは「WMI 戻り値のパース」「EDID マッチングロジック」などの純粋関数部分に留める。
実際の検知は `LGTVSwitcher.Daemon.Windows` を実機で動作させて確認する。

-----

# 5. 運用ガイド（開発者向け）

## ビルドと実行

```bash
# ビルド
dotnet build

# Daemon (デバッグ実行)
cd LGTVSwitcher.Daemon.Windows/bin/Debug/net10.0-windows
./LGTVSwitcher.Daemon.Windows.exe
```

## インストール（Windows）

`scripts/install.ps1` は以下の処理を行う：

1.  旧プロセスの停止・タスク削除
2.  `dotnet publish` (self-contained, win-x64)
3.  `C:\Tools\LGTVSwitcher` への配置
4.  タスクスケジューラへの登録（トリガー: ログオン時）

## 設定変更

`appsettings.json` を変更した場合、デーモンの再起動が必要。
タスクスケジューラで「LGTVSwitcherTask」を終了→実行するか、Windows を再起動する。