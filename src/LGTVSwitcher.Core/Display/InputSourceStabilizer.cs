namespace LGTVSwitcher.Core.Display;

/// <summary>
/// 化けやすい DDC 応答から、入力ソースの判定を安定して確定させる。
/// </summary>
/// <remarks>
/// Apple Silicon から U2725QE の VCP 0x60 を読むと、0x19 の合間に 0x00 / 0x01 / 0x63 / 負値などが混じる（実測で約 3%）。
/// 化けた 1 回を入力切替と取り違えると TV を誤って切り替えるため、次の 2 段で確定させる。
/// 1. 1 回の判定内: 同じ値が <see cref="RequiredIdenticalReads"/> 回続いたときだけ値を確定する。
/// 2. 判定をまたいで: 直近の確定状態から別の状態へ移るのは、その状態が <see cref="RequiredConsecutiveProbes"/> 回続いたときだけ。
/// </remarks>
public sealed class InputSourceStabilizer
{
    public const int RequiredIdenticalReads = 3;
    public const int RequiredConsecutiveProbes = 2;

    private PreferredInputSource _confirmed = PreferredInputSource.Unknown;
    private PreferredInputSource _pending = PreferredInputSource.Unknown;
    private int _pendingCount;

    public PreferredInputSource Confirmed => _confirmed;

    /// <summary>
    /// 連続した読み取り値から、同じ値が <see cref="RequiredIdenticalReads"/> 回続いた最初の値を返す。無ければ null。
    /// 読み取り失敗は null で渡す（連続を途切れさせる）。
    /// </summary>
    public static int? FindStableValue(IEnumerable<int?> reads)
    {
        int? previous = null;
        var run = 0;
        foreach (var read in reads)
        {
            if (read is null)
            {
                previous = null;
                run = 0;
                continue;
            }

            run = read == previous ? run + 1 : 1;
            previous = read;
            if (run >= RequiredIdenticalReads)
            {
                return read;
            }
        }

        return null;
    }

    /// <summary>
    /// 今回の判定結果を取り込み、確定状態を返す。判定できなかった回（Unknown）は確定状態をそのまま返す。
    /// </summary>
    public PreferredInputSource Apply(PreferredInputSource observed)
    {
        if (observed == PreferredInputSource.Unknown)
        {
            _pending = PreferredInputSource.Unknown;
            _pendingCount = 0;
            return _confirmed;
        }

        if (_confirmed == PreferredInputSource.Unknown || observed == _confirmed)
        {
            _confirmed = observed;
            _pending = PreferredInputSource.Unknown;
            _pendingCount = 0;
            return _confirmed;
        }

        _pendingCount = observed == _pending ? _pendingCount + 1 : 1;
        _pending = observed;
        if (_pendingCount >= RequiredConsecutiveProbes)
        {
            _confirmed = observed;
            _pending = PreferredInputSource.Unknown;
            _pendingCount = 0;
        }

        return _confirmed;
    }

    /// <summary>モニタが外れたときなど、直近の状態を引き継ぐべきでないときに呼ぶ。</summary>
    public void Reset()
    {
        _confirmed = PreferredInputSource.Unknown;
        _pending = PreferredInputSource.Unknown;
        _pendingCount = 0;
    }
}
