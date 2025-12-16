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
      - **設定 (`appsettings.json`):** ユーザーが編集する静的な設定（ターゲット入力端子、監視モニター、ログなど）。TVのホスト名/IP は基本設定せず、SSDP 探索で自動検出する（互換用途で `TvHost` は残す）。
      - **状態 (`device-state.json`):** アプリが管理する動的な情報（ペアリング済み `ClientKey`、接続先TVの `USN` など）。`%LOCALAPPDATA%` に分離して保存する。
  - **TV自動検出と接続先固定（SSDP必須 / USN固定）**
      - 起動時に SSDP `M-SEARCH` を送信し、webOS TV候補の `USN` と `LOCATION`（および送信元IP）を収集する。
      - `device-state.json` に `PreferredTvUsn` が存在する場合は、その `USN` に一致するTVへ接続する（`TvHost` は使用しない）。
      - `PreferredTvUsn` が未設定で `ClientKey` が存在する場合は、候補TVに対して登録（register）を試行し、`Registered` になったTVの `USN` を `PreferredTvUsn` として永続化する。
      - 初回（`ClientKey`/`PreferredTvUsn`なし）で複数台が見つかった場合は、誤ったTVにペアリングプロンプトを出さないため、`--pair` 等の診断モードで対象を選択して `PreferredTvUsn` を保存する。

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
lgtv-switcher.slnx
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
  - [x] `LgWebOsClient.Tests`: 異常系レスポンスのテスト拡充
  - [x] CI (GitHub Actions) のセットアップ: ビルドとテストの自動化
  - [ ] 複数TV環境対応（SSDP探索 + USN固定）
      - SSDP による自動検出（`M-SEARCH` / 応答解析 / `USN` での重複排除）
      - `device-state.json` に `PreferredTvUsn` を永続化し、以後は `USN` 一致のTVにのみ接続
      - 初回向けに `--discover` / `--pair` の診断モードを追加（一覧表示・選択・保存）

## Phase 3.5: Cross-platform readiness（クロスプラットフォーム準備）[Planned]

  - [x] OS 依存コードの境界整理  
      - `LGTVSwitcher.Core` / `LGTVSwitcher.DisplayDetection.*` / `LGTVSwitcher.Daemon.*` の責務を明文化  
      - Core から Windows 固有の API / パス参照を排除する

  - [ ] 設定・ストレージ・ログの共通化  
      - 設定ファイルや `device-state.json` の配置パスを OS 抽象（例: `IPathProvider` 等）で統一  
      - ログ設定（Serilog など）を Windows / mac 共通の初期化パターンにまとめる

  - [ ] テストと診断性の補強  
      - `LgWebOsClient.Tests` を Core と組み合わせたテストに拡張（WebSocket モック利用）  
      - `--dry-run` など診断モードを追加し、挙動を追いやすくする

  - [ ] 開発・運用ドキュメント整備  
      - `docs/ARCHITECTURE.md` と簡易 `DEVELOPMENT.md` / `RUNBOOK.md` を作成  
      - 将来の macOS 実装時に読むべき「事前チェックポイント」を明記する

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
cd src/LGTVSwitcher.Daemon.Windows/bin/Debug/net10.0-windows
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
