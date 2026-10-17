namespace LGTVSwitcher.Core.LgTv;

public sealed class LgTvSwitcherOptions
{
    /// <summary>
    /// LG TV のホスト名または IP アドレス。
    /// </summary>
    public string TvHost { get; set; } = "lgwebostv.local";

    /// <summary>
    /// WebSocket ポート番号（webOS TV での `wss://` の既定値は 3001）。
    /// </summary>
    public int TvPort { get; set; } = 3001;

    /// <summary>
    /// 登録済みクライアントキー。空の場合は TV 側にペアリング確認が表示される。
    /// </summary>
    public string? ClientKey { get; set; }

    /// <summary>
    /// SSDP で検出した TV の USN。複数台環境で接続先を固定するために使用する。
    /// </summary>
    public string? PreferredTvUsn { get; set; }

    /// <summary>
    /// 優先モニタがオンラインのときに切り替える入力 ID。
    /// </summary>
    public string TargetInputId { get; set; } = "HDMI_1";

    /// <summary>
    /// 優先モニタがオフラインになった際に切り替えるフォールバック入力 ID（任意）。
    /// </summary>
    public string? FallbackInputId { get; set; }

    /// <summary>
    /// 現在の入力がこの一覧に含まれる場合のみ切り替える（未指定または空なら常に切り替え対象）。
    /// </summary>
    public string[]? AllowedCurrentInputIds { get; set; }

    /// <summary>
    /// ディスプレイ変化がなくても再同期する間隔（秒）。0 以下は無効。
    /// </summary>
    public int SyncIntervalSeconds { get; set; }

    /// <summary>
    /// LG TV への WebSocket 接続（TLS ハンドシェイクを含む）を打ち切るまでの秒数。0 以下は無効。
    /// TV がスタンバイへ入ると TCP は張れても TLS 応答が返らないことがあり、
    /// 上限が無いと接続ロックを握ったまま同期ループ全体が停止する。
    /// </summary>
    public int TvConnectTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// 同期 1 サイクル全体を打ち切るまでの秒数。0 以下は無効。
    /// 接続後に TV が無応答になると送受信が返らないため、サイクルにも上限を設ける。
    /// ペアリング待ち（<see cref="ClientKey"/> 未設定）の間は TV 側の承認に最大 2 分かかるため適用しない。
    /// </summary>
    public int SyncTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// LG TV の切り替えをトリガーするモニタのフレンドリ名（またはデバイス名）。
    /// </summary>
    public string PreferredMonitorName { get; set; } = string.Empty;

    /// <summary>
    /// 優先モニタが「このPCを映している」とみなす映像入力インターフェース名の一覧。
    /// マルチ入力モニタ（例: DELL U2725QE）で、表示ソースが別PC(Mac の USB-C 等)へ移っても
    /// DisplayPort リンクは生き続けるため、列挙有無では判別できない。この入力で実際の表示ソースを判定する。
    /// 名前は "DisplayPort" / "HDMI" / "USB-C" など（<see cref="Display.MonitorInputSourceCatalog"/> 参照）。
    /// 別モニタで値が異なる場合は生の値 "0x1B" / "27" も指定可。
    /// 既定は "DisplayPort"。空にすると DDC 判定を使わず従来の列挙ベースにフォールバックする。
    /// </summary>
    public string[]? PreferredMonitorThisPcInputSources { get; set; } = new[] { "DisplayPort" };

    /// <summary>
    /// 入力ソースのプローブが判定不能（<see cref="Display.PreferredInputSource.Unknown"/>）のとき、
    /// 列挙ベースへフォールバックせず同期を見送るか。既定は false（列挙ベースへフォールバック）。
    /// Windows と Mac の両方で常駐させる構成では true にする。モニタを列挙できても「どちらを映しているか」
    /// 分からない状態で切り替えると、もう一方のデーモンと TV を奪い合うため。
    /// </summary>
    public bool SkipSyncWhenInputSourceUnknown { get; set; }

    /// <summary>
    /// もう一方の PC で動くデーモンのホスト名または IP。空なら単独で動き、常に TV を切り替える。
    /// 設定すると、先に起動していた方だけが TV を切り替え、もう一方はそれが落ちるまで待機する。
    /// </summary>
    public string? PeerHost { get; set; }

    /// <summary>デーモン同士が状態を交換する TCP ポート。両方で同じ値にする。</summary>
    public int PeerPort { get; set; } = 47810;

    /// <summary>相手へ状態を問い合わせる間隔（秒）。</summary>
    public int PeerHeartbeatSeconds { get; set; } = 3;

    /// <summary>この秒数のあいだ相手と連絡が取れなければ、相手は落ちたとみなしてリーダーを引き継ぐ。</summary>
    public int PeerTimeoutSeconds { get; set; } = 10;
}
