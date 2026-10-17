using LGTVSwitcher.Core.Display;

using Xunit;

namespace LGTVSwitcher.Core.Tests;

public class InputSourceStabilizerTests
{
    [Fact]
    public void FindStableValue_FourMatchingReads_ReturnsValue()
    {
        Assert.Equal(25, InputSourceStabilizer.FindStableValue([25, 25, 25, 25]));
    }

    [Fact]
    public void FindStableValue_GarbledReadsInterleaved_StillConfirms()
    {
        // U2725QE を Windows 表示中に Mac から読んだ実測パターン: 0x0F の合間に約 25% 化けた値が混じる。
        Assert.Equal(15, InputSourceStabilizer.FindStableValue([15, 99, 15, -102, 15, 15]));
    }

    [Fact]
    public void FindStableValue_ScatteredGarbage_IsNotConfirmed()
    {
        Assert.Null(InputSourceStabilizer.FindStableValue([25, 1, 25, 0, 25, -7, 99]));
    }

    [Fact]
    public void FindStableValue_FailedReadsAreSkipped()
    {
        Assert.Equal(25, InputSourceStabilizer.FindStableValue([25, null, 25, null, 25, 25]));
    }

    [Fact]
    public void FindStableValue_StopsReadingOnceConfirmed()
    {
        var consumed = 0;
        IEnumerable<int?> Reads()
        {
            while (true)
            {
                consumed++;
                yield return 15;
            }
        }

        Assert.Equal(15, InputSourceStabilizer.FindStableValue(Reads()));
        Assert.Equal(InputSourceStabilizer.RequiredMatchingReads, consumed);
    }

    [Fact]
    public void Apply_FirstObservation_IsConfirmedImmediately()
    {
        var stabilizer = new InputSourceStabilizer();
        Assert.Equal(PreferredInputSource.ThisPc, stabilizer.Apply(PreferredInputSource.ThisPc));
    }

    [Fact]
    public void Apply_SingleContradictingProbe_KeepsConfirmedState()
    {
        var stabilizer = new InputSourceStabilizer();
        stabilizer.Apply(PreferredInputSource.ThisPc);

        Assert.Equal(PreferredInputSource.ThisPc, stabilizer.Apply(PreferredInputSource.OtherSource));
        Assert.Equal(PreferredInputSource.ThisPc, stabilizer.Apply(PreferredInputSource.ThisPc));
    }

    [Fact]
    public void Apply_TwoConsecutiveContradictingProbes_SwitchesState()
    {
        var stabilizer = new InputSourceStabilizer();
        stabilizer.Apply(PreferredInputSource.ThisPc);

        stabilizer.Apply(PreferredInputSource.OtherSource);
        Assert.Equal(PreferredInputSource.OtherSource, stabilizer.Apply(PreferredInputSource.OtherSource));
    }

    [Fact]
    public void Apply_UnknownBetweenContradictions_ResetsPending()
    {
        var stabilizer = new InputSourceStabilizer();
        stabilizer.Apply(PreferredInputSource.ThisPc);

        stabilizer.Apply(PreferredInputSource.OtherSource);
        Assert.Equal(PreferredInputSource.ThisPc, stabilizer.Apply(PreferredInputSource.Unknown));
        Assert.Equal(PreferredInputSource.ThisPc, stabilizer.Apply(PreferredInputSource.OtherSource));
    }

    [Fact]
    public void Reset_ForgetsConfirmedState()
    {
        var stabilizer = new InputSourceStabilizer();
        stabilizer.Apply(PreferredInputSource.ThisPc);
        stabilizer.Reset();

        Assert.Equal(PreferredInputSource.Unknown, stabilizer.Confirmed);
        Assert.Equal(PreferredInputSource.OtherSource, stabilizer.Apply(PreferredInputSource.OtherSource));
    }
}
