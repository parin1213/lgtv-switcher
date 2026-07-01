namespace LGTVSwitcher.Core.Display;

/// <summary>
/// 優先モニタが「今このPCの映像入力を映しているか」を表す状態。
/// </summary>
/// <remarks>
/// DELL U2725QE のようなマルチ入力/KVMモニタは、表示ソースを別PC（例: Mac の USB-C）へ
/// 切り替えても、Windows への DisplayPort リンクを生かしたままにする。そのため
/// <see cref="MonitorSnapshot"/> の列挙有無だけでは「このPCを映しているか」を判別できない。
/// この状態は DDC/CI（VCP 0x60 = Input Source）等の別経路で取得する。
/// </remarks>
public enum PreferredInputSource
{
    /// <summary>判定できなかった（プローブ非対応・読み取り失敗・未接続など）。</summary>
    Unknown = 0,

    /// <summary>優先モニタはこのPCの入力（例: DisplayPort）を映している。</summary>
    ThisPc = 1,

    /// <summary>優先モニタは別ソースの入力（例: Mac の USB-C）を映している。</summary>
    OtherSource = 2,
}

/// <summary>
/// 優先モニタが今どの映像入力を映しているかを問い合わせるプローブ。
/// OS 依存の実装（Windows は DDC/CI）を注入する。
/// </summary>
public interface IPreferredInputSourceProbe
{
    /// <summary>
    /// 優先モニタの現在の入力ソース状態を返す。判定不能なら <see cref="PreferredInputSource.Unknown"/>。
    /// </summary>
    PreferredInputSource Probe();
}

/// <summary>
/// 常に <see cref="PreferredInputSource.Unknown"/> を返す既定プローブ。
/// DDC を持たない環境（macOS など）や未設定時に使用し、既存の列挙ベース判定へフォールバックさせる。
/// </summary>
public sealed class NullPreferredInputSourceProbe : IPreferredInputSourceProbe
{
    public PreferredInputSource Probe() => PreferredInputSource.Unknown;
}
