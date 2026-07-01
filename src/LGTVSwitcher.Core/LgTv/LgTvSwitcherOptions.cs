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
}
