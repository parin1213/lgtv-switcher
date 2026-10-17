# Repository Guidelines

## プロジェクト構成

- **LGTVSwitcher.Core** (`net10.0`): ドメインモデル（`DisplaySnapshot`）、設定（`LgTvSwitcherOptions`）、メインロジック（`DisplaySyncWorker`）、スナップショット提供IF（`IDisplaySnapshotProvider`）、LG webOS クライアント（`LgWebOs/`）、KeyStore（`JsonFileClientKeyStore`）。OS 依存コードを含まない。
- **LGTVSwitcher.Daemon.Windows** (`net10.0-windows`): Win32/WMI によるモニター検知（`DisplayDetection/`）、Serilog ログ、Windows サービスホスト。
- **LgtvSwitcher.MacOS** (`net10.0`): macOS デーモンホスト（LaunchAgent 常駐）。DDC は `m1ddc` CLI 経由（`M1ddcClient`）。`MacDisplayWatcher` は 2 秒おきに外部ディスプレイと入力ソースをポーリングする（モニタ側の入力切替では macOS のディスプレイ構成が変わらず、CGDisplay の再構成コールバックでは拾えないため）。`MacDdcInputSourceProbe` は優先モニタが列挙に居ないとき直近値を使わず Unknown を返す。
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
- **タイムアウト（ハング防止）**: TV がスタンバイへ入ると TCP は張れても TLS ハンドシェイクや送受信の応答が返らない。`TvConnectTimeoutSeconds`（既定 10 秒 / `LgTvSession.ConnectTransportAsync`）で接続を、`SyncTimeoutSeconds`（既定 30 秒 / `DisplaySyncWorker.TrySyncAsync`）で同期 1 サイクル全体を打ち切る。**どちらも外してはならない**——上限が無いと接続ロックを握ったまま同期ループが永久停止し、プロセスは生きていてディスプレイ検知も動くのに切替だけが死ぬ。打ち切りは TV 到達不可と同じ扱いにして次サイクルで張り直す。ペアリング待ち（`ClientKey` 未設定）だけは TV 側の承認に最大 2 分かかるため、サイクルのウォッチドッグを適用しない。
- **入力ソース判定（DDC/CI）**: 優先モニタが「今このPCを映しているか」は列挙有無では判別できない（USB-C/KVM モニタは別PC表示中も DP リンクを維持するため）。`IPreferredInputSourceProbe`（Windows: `DdcInputSourceProbe` が VCP 0x60 を読む）で都度判定し、`ThisPc`→online 扱い / `OtherSource`→offline 扱い / `Unknown`→列挙ベースへフォールバック、として同期判定に反映する。**この判定は同期の度に都度実行すること**（Mac への表示切替はディスプレイイベントを発火しないため、スナップショットに焼き込むと定期同期が誤って TV を奪う）。DDC は稀に失敗するのでリトライ＋直近確定値の保持で安定させる。Windows と Mac の両方で常駐させる構成では `SkipSyncWhenInputSourceUnknown=true` とし、`Unknown` のとき列挙ベースへフォールバックせず同期を見送る（推測で切り替えると、もう一方のデーモンと TV を奪い合うため）。
- **Mac 側の DDC とネットワーク**: Mac からの DDC 応答は化けるため、`InputSourceStabilizer` の多数決（7 回中 4 回一致）＋ 2 回連続一致で確定し、判定不能の回は数えない。U2725QE を Windows 表示にすると Dell 経由の有線 LAN が Windows 側へ移り、Mac は Wi-Fi だけになって SSDP も mDNS も届かない。そのため `LgTvSession` は最後に接続できた TV の IP（`LastKnownTvAddress`）へフォールバックする。
- **設定の配列に初期値を入れない**: .NET の設定バインドは配列をプロパティ初期値へ追記する。既定値は `ResolveThisPcInputSources()` のように読み出し側で補う。
- **TLS/ClientKey**:
  - webOS の自己署名証明書対策として `DefaultWebSocketTransport` は wss 証明書検証を緩和する仕様を維持する。
  - `clientKey` / `preferredTvUsn` は sidecar `state.json`（Win: `%LOCALAPPDATA%/LgtvSwitcher/state.json`、Mac: `~/Library/Application Support/LgtvSwitcher/state.json`）に永続化。`appsettings.json` には置かない。
- **WindowsDisplaySnapshotProvider のメッセージループ**:
  - 非 UI の STA メッセージループを持つ Hidden Window を使用する。Windows の仕様上必須であり、非同期化・スレッド切替で破壊しないこと。
  - `DisplayDetection/` 内で `Task.Run` や `ConfigureAwait` の軽率な使用は禁止。
- **Win/Mac の連携（リーダー選出）**: `PeerHost` を設定すると `PeerCoordinator` が TCP（既定 47810）で相手デーモンと状態を交換し、TV を切り替えるのはリーダーだけにする（`DisplaySyncWorker` は `ILeadershipState.IsLeader` が false なら同期を見送る）。規則は `LeaderElection.Decide`: 連絡不能なら自分がリーダー／判定できない側（`ILeadershipEligibility`）は譲る／先にリーダーになった・先に起動した方が残る。**両方が切り替える構成に戻さないこと**——Mac の DDC 応答は化けるため、両者が切り替えると TV が交互に入れ替わり続ける。
- **ログ**: 状態遷移・実際の入力切替のみ Information。定常反復（`Already connected`/`already set; no switch`/SSDP の discover start・summary・final result・`No input mapping…skipping`）は Debug に留めてログ肥大を防ぐ。WebSocket 例外は Warning 1 行、詳細は Debug。**TV 到達不可（ネットワーク断・未検出/オフライン=`LgTvRegistrationException`）は正常運用でも起きるため、遷移時に一度だけ Warning、以降は Debug**（`_tvUnavailableLogged` で管理。復帰時に Information）。SSDP 探索は WSL/Tailscale 等の仮想 NIC を `SsdpInterfaceFilter` で除外する。

## テスト方針
- Core: DisplaySyncWorker のオンライン/オフライン、stale 無視、冗長スイッチ抑止、例外スキップを UT で担保。無応答 TV でのハング打ち切り（接続タイムアウト／サイクルのウォッチドッグ）も、ハングを再現する fake で UT 化する。
- DisplayDetection.Windows: Win32MonitorEnumerator のトークン抽出・接続種別マッピングを UT。OS 依存部分は実機検証。
- LgWebOsClient: レスポンスパーサーの正常/エラー/登録応答を UT。トランスポートはモック差し替え可能に。

## 言語・レビュー
- すべての説明・レビュー・追加ドキュメントは日本語で統一。コード識別子は英語で OK。
