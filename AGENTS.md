# Repository Guidelines

## プロジェクト構成

- **LGTVSwitcher.Core** (`net10.0`): ドメインモデル（`DisplaySnapshot`）、設定（`LgTvSwitcherOptions`）、メインロジック（`DisplaySyncWorker`）、スナップショット提供IF（`IDisplaySnapshotProvider`）、LG webOS クライアント（`LgWebOs/`）、KeyStore（`JsonFileClientKeyStore`）。OS 依存コードを含まない。
- **LGTVSwitcher.Daemon.Windows** (`net10.0-windows`): Win32/WMI によるモニター検知（`DisplayDetection/`）、Serilog ログ、Windows サービスホスト。
- **LgtvSwitcher.MacOS** (`net10.0`): macOS デーモンホスト。`MacDisplayWatcher` は Phase 3 未実装（`NotImplementedException`）。
- **tests/**: Core / DisplayDetection.Windows / LgWebOsClient の単体テスト（計49本）。

## ビルド・実行
- `dotnet build lgtv-switcher.slnx`
- `dotnet test` （各テストプロジェクト）
- Daemon: `dotnet run --project src/LGTVSwitcher.Daemon.Windows`
- インストールスクリプト: `scripts/install.ps1`（管理者）、アンインストール: `scripts/uninstall.ps1`

## コーディングスタイル
- C# は 4 スペース。`using` は System からアルファベット順。
- クラス/公開メンバーは PascalCase、ローカル/プライベートは camelCase（フィールドは `_` 接頭）。
- 設定キーは PascalCase（例: TargetInputId）。
- コメント・ドキュメント・コミットメッセージは日本語で。

## アーキテクチャ指針
- **同期ループ**: `DisplaySyncWorker` は `Channel<DisplaySnapshot>` + `PeriodicTimer`（800ms debounce）で同期を行う。Rx は撤去済み。
- **Stale/Noise 除去**:
  - `PreferredMonitorEdidKey` が空、または `ConnectionKind=Unknown` のスナップショットは無視。
  - Preferred monitor 以外のモニタ変化でも Sync が走ることを期待。この動作を破壊しないこと。
  - ネットワーク例外（WebSocket/HttpRequest/Socket）はそのスナップショットを捨て、ループは継続。
- **LGTV 同期**: オンライン/オフライン双方で Target/Fallback を自動切替。既に目標入力なら冪等スキップ。
- **TLS/ClientKey**:
  - webOS の自己署名証明書対策として `DefaultWebSocketTransport` は wss 証明書検証を緩和する仕様を維持する。
  - `clientKey` / `preferredTvUsn` は sidecar `state.json`（Win: `%LOCALAPPDATA%/LgtvSwitcher/state.json`、Mac: `~/Library/Application Support/LgtvSwitcher/state.json`）に永続化。`appsettings.json` には置かない。
- **WindowsDisplaySnapshotProvider のメッセージループ**:
  - 非 UI の STA メッセージループを持つ Hidden Window を使用する。Windows の仕様上必須であり、非同期化・スレッド切替で破壊しないこと。
  - `DisplayDetection/` 内で `Task.Run` や `ConfigureAwait` の軽率な使用は禁止。
- **ログ**: 初期スナップショットは Information、それ以外は Debug。WebSocket 例外は Warning 1 行、詳細は Debug。

## テスト方針
- Core: DisplaySyncWorker のオンライン/オフライン、stale 無視、冗長スイッチ抑止、例外スキップを UT で担保。
- DisplayDetection.Windows: Win32MonitorEnumerator のトークン抽出・接続種別マッピングを UT。OS 依存部分は実機検証。
- LgWebOsClient: レスポンスパーサーの正常/エラー/登録応答を UT。トランスポートはモック差し替え可能に。

## 言語・レビュー
- すべての説明・レビュー・追加ドキュメントは日本語で統一。コード識別子は英語で OK。
