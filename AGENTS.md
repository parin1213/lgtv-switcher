# Repository Guidelines

## Project Structure & Module Organization
- `LGTVSwitcher.Core`: モニター状態のドメインモデル／ワーカー／設定を保持し、OS 依存コードを持たない。
- `LGTVSwitcher.DisplayDetection.Windows`: Hidden window + Win32/WMI による接続検知。列挙ロジックはここに閉じ込める。
- `LGTVSwitcher.LgWebOsClient`: LG webOS 向け WebSocket クライアント、シリアライザー、トランスポート層、`ILgTvController` 実装を提供。
- `LGTVSwitcher.Sandbox.Windows`: Generic Host ベースの検証用コンソール。`appsettings.json` にペアリング済み `ClientKey` が保存されるため秘密情報として扱う。

## Build, Test, and Development Commands
- `dotnet build lgtv-switcher.slnx` : ソリューション全体の復元＋ビルド。
- `dotnet run --project LGTVSwitcher.Sandbox.Windows` : 監視対象モニターの接続を観測し、LG TV の入力切替をデバッグ実行。
- `dotnet format` : `.editorconfig` に基づき自動整形。
- Windows 実機では `LGTVSwitcher.Sandbox.Windows/bin/Debug/net10.0-windows/` の EXE を直接起動して動作確認する。

## Coding Style & Naming Conventions
- C# は 4 スペース、XML は 2 スペースでインデント。暗黙/明示の型は文脈に応じて使い分けるが、意図が読みやすい方を優先。
- `using` は `System` を先頭にグループ化し、アルファベット順を維持。
- クラス／public メンバーは PascalCase、ローカル変数／private フィールドは camelCase。設定キー (`PreferredMonitorName` など) も PascalCase。
- `dotnet_style_readonly_field=true` などアナライザー警告を解決し、`dotnet format` で最終確認。

## Testing Guidelines
- 現状テストプロジェクトは未整備のため、`LGTVSwitcher.Sandbox.Windows` を使って回帰確認を行う。ターゲットモニターの接続を変えつつ `TargetInputId`/`FallbackInputId` が期待通り LGTV に反映されるかを実機で確認する。
- 将来的に自動テストを追加する場合は `LGTVSwitcher.Tests` (xUnit) を新設し、`dotnet test` を採用。テストメソッド名は `MethodName_State_Outcome` 形式を推奨。

## Commit & Pull Request Guidelines
- Conventional Commits (`feat(refactor): ...`, `docs: ...` など) を採用し、スコープには対象プロジェクト名を含める。
- コミットは小さくまとめ、設定ファイルの変更は生成物と分離する。
- PR では影響するモニター構成や再現手順、コンソールログを記載し、関連 Issue をリンクする。UI や動作の変更がある場合は LGTV の入力遷移を記録する。
- このリポジトリに関するユーザ対応やドキュメントは必ず日本語で行う。
- レビューを依頼し、CI や手動検証で動作確認が完了してからマージする。

## Style & Architectural Preferences
- **Rx パイプライン**: ディスプレイ検知フローは `IObservable<DisplaySnapshotNotification>` を軸に、`Buffer`/`DistinctUntilChanged` で短時間のノイズと重複通知を抑制する。
- **責務分離**: Win32/WMI 列挙は `WindowsDisplaySnapshotProvider` に集約し、`DisplaySyncWorker` や `DisplayChangeLoggingService` は Provider を購読するだけにして列挙を二重化しない。
- **ログ方針**: すべて `ILogger` で記録。初期スナップショット (`initial-startup`) は `Information`、それ以外は `Debug`。WebSocket 例外は Warning で 1 行、詳細は Debug で補足。
- **LGTV 同期**: オンライン/オフライン双方で Target/Fallback 入力を自動切替。既に目標入力なら冪等にスキップ。Rx 購読を先に張ってから `StartAsync` を呼び、初期スナップショットも必ず処理する。
- **JSON/Transport エラー処理**: `LgTvController` は JSON パースを `LgTvResponseParser` に委譲し、WebSocket 切断時は再接続＋再登録＋再送で復旧する。
- **出力・レビュー言語**: ユーザへの説明、レビューコメント、追加ドキュメントは日本語で統一する。
