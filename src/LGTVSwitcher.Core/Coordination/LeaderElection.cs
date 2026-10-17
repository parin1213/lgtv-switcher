namespace LGTVSwitcher.Core.Coordination;

/// <summary>TV を切り替える役（リーダー）を決めるときの、各デーモンの役割。</summary>
public enum PeerRole
{
    /// <summary>起動直後で、相手の状態をまだ確かめていない。</summary>
    Starting = 0,

    /// <summary>TV を切り替える。</summary>
    Leader = 1,

    /// <summary>リーダーが生きている間は TV に触れない。</summary>
    Follower = 2,
}

/// <summary>相手デーモンとやり取りする自分の状態。</summary>
/// <param name="NodeId">マシン名。開始時刻まで同じときの決着に使う。</param>
/// <param name="Role">現在の役割。</param>
/// <param name="StartedAt">デーモンの起動時刻（UTC）。</param>
/// <param name="LeaderSince">リーダーになった時刻（UTC）。リーダーでなければ null。</param>
/// <param name="Eligible">優先モニタの表示元を判定できていて、リーダーを務められるか。</param>
public sealed record PeerStatus(
    string NodeId,
    PeerRole Role,
    DateTimeOffset StartedAt,
    DateTimeOffset? LeaderSince,
    bool Eligible = true);

/// <summary>
/// Windows と Mac のデーモンのうち、どちらが TV を切り替えるかを決める。
/// </summary>
/// <remarks>
/// 両方が同時に TV を切り替えると、DDC の読み違い 1 回で TV の入力が交互に入れ替わり続ける。
/// 「先に起動していた方がリーダーを持ち続ける」を規則とし、次の順で決める。
/// 1. 相手と連絡が取れない → 自分がリーダー（相手が落ちた・眠った）。
/// 2. 片方だけが判定できない（Mac を外した等） → 判定できる方がリーダー。
/// 3. 相手がリーダー → 自分がリーダーでなければフォロワー。
///    両方リーダー（ネットワーク断の後など）なら、先にリーダーになった方が残る。
/// 4. 相手がリーダーでない → 自分がリーダーならそのまま。どちらもリーダーでなければ、先に起動した方がなる。
/// 時刻が同じときはマシン名の順で決める。時刻比較は両マシンの時計が NTP で合っている前提。
/// </remarks>
public static class LeaderElection
{
    public static PeerRole Decide(PeerStatus self, PeerStatus? peer)
    {
        if (peer is null)
        {
            return PeerRole.Leader;
        }

        if (self.Eligible != peer.Eligible)
        {
            return self.Eligible ? PeerRole.Leader : PeerRole.Follower;
        }

        if (peer.Role == PeerRole.Leader)
        {
            if (self.Role != PeerRole.Leader)
            {
                return PeerRole.Follower;
            }

            return IsEarlier(self.LeaderSince ?? self.StartedAt, self.NodeId, peer.LeaderSince ?? peer.StartedAt, peer.NodeId)
                ? PeerRole.Leader
                : PeerRole.Follower;
        }

        if (self.Role == PeerRole.Leader)
        {
            return PeerRole.Leader;
        }

        return IsEarlier(self.StartedAt, self.NodeId, peer.StartedAt, peer.NodeId)
            ? PeerRole.Leader
            : PeerRole.Follower;
    }

    private static bool IsEarlier(DateTimeOffset selfTime, string selfId, DateTimeOffset peerTime, string peerId)
    {
        if (selfTime != peerTime)
        {
            return selfTime < peerTime;
        }

        return string.CompareOrdinal(selfId, peerId) < 0;
    }
}
