# LGTV Switcher 開発計画（Windows/Mac 両対応）

本ドキュメントは、Windows/Mac 両対応の .NET アプリケーションとして、
**U2725QE の接続状態（自動切換）に連動して LG 49NANO86JNA の入力切替を行う常駐ソフトウェア**を開発するための設計・実証計画である。

本計画では **.NET Generic Host（DI／HostedService／設定／ロギング）を基盤とする構成**を採用し、
Windows Service / macOS LaunchDaemon の両方で「ログイン前から動作するバックグラウンド制御」を可能にする。

また、内部連携は **DI で紐付いたインスタンス間のイベント／インターフェースによるバケツリレー構造**とし、
OS 差分はプラットフォーム実装に閉じ込める。

---

# 🎯 必要な機能とコンポーネント

1. **ディスプレイの増減イベントの検知**（Windows/Mac）
2. **LG TV（webOS）への入力切替指示**
3. **ログイン前から動作する特権環境での起動**（Windows Service / LaunchDaemon）

上記を満たすため、機能ごとに段階的なプロトタイピングとテストを行う。

# 🧩 システム構成の最終イメージ（Generic Host ベース）

```
┌─────────────────────────────┐
│        UI層（任意：Tray/メニューバー）       │
└─────────────┬──────────┘
               │ IPC
┌──────────────▼─────────────┐
│     バックグラウンドサービス本体 (.NET Host) │
│  - IDisplayMonitor (Win/Mac 切替)             │
│  - ILgTvController (webOS API)                │
│  - DisplaySyncWorker（イベント連携）          │
│  - 設定/IOptions                               │
└─────────────┬────────────┘
        OSごとの実装
┌──────────────▼───────────┐
│ Windows:  HiddenWindow + Win32/WMI, Service   │
└──────────────────────────────────────────────┘
┌──────────────────────────────────────────────┐
│ macOS: CGDisplayCallback + launchd Daemon     │
└──────────────────────────────────────────────┘
```

### 🔑 連携の肝（今の説明で得た知見）

* DI (`AddSingleton`, `AddHostedService`) は **new の配線図を登録するだけ**
* Host 起動時に依存を辿って自動的にインスタンス生成
* DisplayMonitor のイベントを Worker が購読し、LgTvController を呼び出す
* 本質的には **インターフェース＋イベントのバケツリレー構造**
* OWIN ミドルウェアチェーンの進化版のようなもの

この理解を元に、以下の実証計画を進める。

---

# 1. ディスプレイの増減イベント検知の実証

Windows → Mac の順に検証する。

## 1-1. OSレベルの挙動把握と要件整理

* Win32 (`WM_DISPLAYCHANGE`, `WM_DEVICECHANGE`) と `SystemEvents.DisplaySettingsChanged` の違いを調査
* ケース別（USB-Cドック、直接接続、RDP）で必要なイベント粒度を決定
* モニタ構成を `MonitorSnapshot` DTO として表現
* Mac 側は `CGDisplayRegisterReconfigurationCallback` を利用予定 → 抽象化ポイントを整理

## 1-2. Windows向けプロトタイプ

* `lgtv-switcher.Experimental/DisplayDetection/WindowsMonitorDetector.cs` を追加
* `IDisplayChangeDetector` を定義し、イベント通知を実装
* Win32 メッセージループ隠蔽用 `HiddenWindowMessagePump` を作る
* コンソールで JSON ダンプを行い挙動確認

## 1-3. 共通インターフェースとテスト方針

* `LGTVSwitcher.Daemon.Common` に抽象 (`IDisplaySnapshotProvider`, `IDisplayChangeChannel`) を配置
* Windows 実装は UT が困難 → 列挙ロジックを差し替え可能にしてテスト

## 1-4. Mac向け準備

* `LGTVSwitcher.Platform.Mac` の雛形プロジェクトを追加
* `CGGetOnlineDisplayList` と Windows 側の識別子対応表を検討
* イベント比較ロジックを整理

---

# 2. LGTV 入力切替の実証

## 2-1. WebOS API 調査

* `lgtv2` 互換 WebSocket API を実機で確認
* `client-key` の安全管理方法を決める（暗号化 JSON / ProgramData 配置）
* 入力切替に必要なコマンド一覧を作成

## 2-2. クライアント実装方針

* `ConfiguredLgTvClient`（仮）の責務を定義

  * 接続・ペアリング
  * 入力切替
  * 再接続・タイムアウト
* DI 登録用 `ServiceCollectionExtensions` を作成
* `ILgTvTransport` を抽象化して UT 可能にする

## 2-3. 入力切替ロジックの試験

* Display イベント → TV 切替のシナリオ確認
* ネットワーク不通時のリトライ／通知方法を検討
* 遅延測定し、閾値を決定

---

# 3. ログイン前から動作する特権サービス化

## 3-1. Windows Service 化

* `LGTVSwitcher.Daemon.Windows` プロジェクトを作成
* `UseWindowsService()` を有効化
* PowerShell インストーラ（`sc.exe create`）を作る
* 設定ファイルは `C:\ProgramData\LGTVSwitcher\` 固定
* ログは Event Log + ファイル（Serilog）へ出力

## 3-2. macOS LaunchDaemon 化

* `LGTVSwitcher.Daemon.Mac` プロジェクトを作成
* `/Library/LaunchDaemons/com.lgtv.switcher.plist` テンプレを用意
* `launchctl bootstrap / bootout` スクリプトを作る
* Self-contained publish の配布方法を検討
* macOS 特有の権限の要否を確認

## 3-3. 共通ホスティング構造

* `LGTVSwitcher.Daemon.Common` に

  * 設定読込
  * DisplayDetector 登録
  * LGTV クライアント登録
  * DisplaySyncWorker
    をまとめる
* 設定は `IOptions<LgTvSwitcherOptions>` で扱う
* 停止処理／例外再起動ポリシーを整理

---

# 4. 統合・検証と移行計画

* `/docs` に各種ドキュメントを整理
* Windows/Mac の手動テストシナリオを `tests/manual/` に追加
* CI は `dotnet format` / `dotnet build` のみ最小構成
* 初期リリースは Windows 版のみ
* Display 検知調査完了後に Mac 版ベータへ移行

---

# 付録：Generic Host を用いた連携の理解

* DI に登録されたインターフェース実装が、Host 起動時に自動生成される
* DisplayMonitor → Worker → LgTvController は **イベントとメソッド呼び出しのバケツリレー**
* OWIN のミドルウェアチェーンと似た構造だが、ライフサイクルが長く常駐用途向き
* Windows/Mac の差分は `IDisplayMonitor` 実装内だけに閉じ込める

以上の計画に基づき、各ステップを順に実装していく。
