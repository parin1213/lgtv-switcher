namespace LGTVSwitcher.Core.Coordination;

/// <summary>このデーモンが今 TV を切り替えてよいか。</summary>
public interface ILeadershipState
{
    bool IsLeader { get; }
}

/// <summary>相手デーモンを設定していない（単独で動かす）ときの既定。常にリーダー。</summary>
public sealed class AlwaysLeader : ILeadershipState
{
    public bool IsLeader => true;
}

/// <summary>
/// このデーモンがリーダーを務められるか（優先モニタの表示元を判定できているか）。
/// </summary>
/// <remarks>
/// Mac を Dell から外すと Mac は判定不能になり TV に触れない。その Mac がリーダーのままだと
/// Windows も TV を切り替えられないため、判定できない側はリーダーを相手に譲る。
/// </remarks>
public interface ILeadershipEligibility
{
    bool IsEligible { get; }
}

/// <summary>判定不能でも列挙ベースで切り替えられる環境（Windows）の既定。常に務められる。</summary>
public sealed class AlwaysEligible : ILeadershipEligibility
{
    public bool IsEligible => true;
}
