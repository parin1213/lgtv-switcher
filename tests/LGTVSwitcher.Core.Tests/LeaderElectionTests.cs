using LGTVSwitcher.Core.Coordination;

using Xunit;

namespace LGTVSwitcher.Core.Tests;

public class LeaderElectionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

    private static PeerStatus Node(string id, PeerRole role, int startedAtSec, int? leaderSinceSec = null)
        => new(id, role, T0.AddSeconds(startedAtSec), leaderSinceSec is { } s ? T0.AddSeconds(s) : null);

    [Fact]
    public void PeerUnreachable_BecomesLeader()
    {
        Assert.Equal(PeerRole.Leader, LeaderElection.Decide(Node("MAC", PeerRole.Starting, 10), null));
        Assert.Equal(PeerRole.Leader, LeaderElection.Decide(Node("MAC", PeerRole.Follower, 10), null));
    }

    [Fact]
    public void PeerIsLeader_StartingNodeBecomesFollower_EvenIfStartedEarlier()
    {
        // 先に起動していても、相手が既にリーダーなら奪わない（再起動した側は従う）。
        var self = Node("MAC", PeerRole.Starting, 0);
        var peer = Node("WIN", PeerRole.Leader, 5, 5);
        Assert.Equal(PeerRole.Follower, LeaderElection.Decide(self, peer));
    }

    [Fact]
    public void BothStarting_EarlierStartWins()
    {
        Assert.Equal(PeerRole.Leader, LeaderElection.Decide(Node("WIN", PeerRole.Starting, 0), Node("MAC", PeerRole.Starting, 1)));
        Assert.Equal(PeerRole.Follower, LeaderElection.Decide(Node("MAC", PeerRole.Starting, 1), Node("WIN", PeerRole.Starting, 0)));
    }

    [Fact]
    public void BothStarting_SameTime_NodeIdBreaksTie()
    {
        Assert.Equal(PeerRole.Leader, LeaderElection.Decide(Node("A", PeerRole.Starting, 0), Node("B", PeerRole.Starting, 0)));
        Assert.Equal(PeerRole.Follower, LeaderElection.Decide(Node("B", PeerRole.Starting, 0), Node("A", PeerRole.Starting, 0)));
    }

    [Fact]
    public void LeaderStaysLeader_WhenPeerIsFollower()
    {
        Assert.Equal(PeerRole.Leader, LeaderElection.Decide(Node("MAC", PeerRole.Leader, 9, 9), Node("WIN", PeerRole.Follower, 0)));
    }

    [Fact]
    public void IneligibleLeader_YieldsToEligiblePeer()
    {
        // Mac を Dell から外して判定不能になったら、先にリーダーだったとしても Windows に譲る。
        var mac = Node("MAC", PeerRole.Leader, 0, 0) with { Eligible = false };
        var win = Node("WIN", PeerRole.Follower, 5);
        Assert.Equal(PeerRole.Follower, LeaderElection.Decide(mac, win));
        Assert.Equal(PeerRole.Leader, LeaderElection.Decide(win, mac));
    }

    [Fact]
    public void IneligibleNode_WithUnreachablePeer_StillLeads()
    {
        // 相手が居なければ自分しか居ない。判定できない間は同期側が TV に触れないので害は無い。
        Assert.Equal(PeerRole.Leader, LeaderElection.Decide(Node("MAC", PeerRole.Follower, 0) with { Eligible = false }, null));
    }

    [Fact]
    public void BothLeaders_EarlierLeaderSinceWins()
    {
        // ネットワーク断の間に両方リーダーになった場合、先にリーダーになっていた方が残る。
        var older = Node("WIN", PeerRole.Leader, 0, 0);
        var newer = Node("MAC", PeerRole.Leader, 1, 100);
        Assert.Equal(PeerRole.Leader, LeaderElection.Decide(older, newer));
        Assert.Equal(PeerRole.Follower, LeaderElection.Decide(newer, older));
    }
}
