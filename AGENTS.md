# Repository Guidelines

## プロジェクト構成
- **LGTVSwitcher.Core**: ドメインモデル（DisplaySnapshot など）、設定（LgTvSwitcherOptions）、
  メインロジック（DisplaySyncWorker）、スナップショット提供IF（IDisplaySnapshotProvider）。OS 依存コードを含まない。
- **LGTVSwitcher.DisplayDetection.Windows**: Win32/WMI によるモニター検知。Hidden Window メッセージループ（WindowsMessagePump）、列挙（Win32MonitorEnumerator）、スナップショット提供（WindowsDisplaySnapshotProvider）。
- **LGTVSwitcher.LgWebOsClient**: LG webOS 向け WebSocket クライアント。プロトコルパーサー（LgTvResponseParser）、トランスポート層（DefaultWebSocketTransport）、ILgTvController 実装。
- **LGTVSwitcher.Daemon.Windows**: 本番ホスト。設定読み込み、デバイス状態保存、Serilog によるログ出力。Windows サービスとして実行。
- **tests**: Core / DisplayDetection.Windows / LgWebOsClient の単体テスト。

## ビルド・実行
- `dotnet build lgtv-switcher.slnx`
- `dotnet test` （各テストプロジェクト）
- Daemon: `dotnet run --project LGTVSwitcher.Daemon.Windows`
- インストールスクリプト: `scripts/install.ps1`（管理者）、アンインストール: `scripts/uninstall.ps1`

## コーディングスタイル
- C# は 4 スペース。`using` は System からアルファベット順。
- クラス/公開メンバーは PascalCase、ローカル/プライベートは camelCase（フィールドは `_` 接頭）。
- 設定キーは PascalCase（例: TargetInputId）。
- コメント・ドキュメント・コミットメッセージは日本語で。

## アーキテクチャ指針
- **Rx パイプライン**: DisplaySyncWorker は `IObservable<DisplaySnapshotNotification>` を 800ms Buffer → 最後の 1 件 → フィルタ → DistinctUntilChanged で処理し、LGTV 同期を行う。
- **Stale/Noise 除去**:
  - PreferredMonitorEdidKey が空、または ConnectionKind=Unknown のスナップショットは無視。
  - Snapshot.Timestamp が 5 秒超古いものは stale として破棄。
  - `DistinctUntilChanged` 比較軸: PreferredMonitorOnline / PreferredMonitorEdidKey / ConnectionKind / 対象入力。
  - Preferred monitor 以外のモニタ変化でも Sync が走ることを期待しています。この動作は破壊しないこと
  - ネットワーク例外（WebSocket/HttpRequest/Socket）はそのスナップショットを捨て、パイプラインは継続。最新状態のみ適用。
- **LGTV 同期**: オンライン/オフライン双方で Target/Fallback を自動切替。既に目標入力なら冪等スキップ。購読を張ってから StartAsync を呼び、初期スナップショットも処理する。
- **TLS/ClientKey 前提**:
  - webOS の自己署名証明書対策として `DefaultWebSocketTransport` は wss の証明書検証を緩和する仕様を維持する。
  - `client-key` は `%LOCALAPPDATA%/LGTVSwitcher/device-state.json` に永続化し、`appsettings.json` には置かない。起動時は `ConfigureAppConfiguration` でこの state ファイルを読み込む。
- WindowsDisplaySnapshotProvider のメッセージループ仕様
  - WindowsDisplaySnapshotProvider は 非UIの STA メッセージループ を持つ Hidden Window を使用する
  - この構造は Windows の仕様上必須であり、非同期化・スレッド切替で破壊しないこと
  - ※ 実装では Monitor 配列全体の構造差異（Count や他モニタの接続変化）も比較対象に含まれます。
  - DisplayDetection.Windows では Task.Run や ConfigureAwait の軽率な使用は禁止
  - **（重要）このメッセージループは STA スレッドでなければ動作しない。**
- **ログ**: 初期スナップショットは Information、それ以外は Debug を基本。WebSocket 例外は Warning 1 行、詳細は Debug。Serilog 設定は appsettings で上書き可能（File/Console）。

## テスト方針
- Core: DisplaySyncWorker のオンライン/オフライン、stale 無視、冗長スイッチ抑止、例外スキップを UT で担保。
- DisplayDetection.Windows: Win32MonitorEnumerator のトークン抽出・接続種別マッピングを UT。OS 依存部分は実機検証。
- LgWebOsClient: レスポンスパーサーの正常/エラー/登録応答を UT。トランスポートはモック差し替え可能に。

## 言語・レビュー
- すべての説明・レビュー・追加ドキュメントは日本語で統一。コード識別子は英語で OK。
