using Aetherphone.Apps.Casino.Machines;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Casino;
using Xunit;

namespace Aetherphone.Tests;

public sealed class MachineTraceCycleTests
{
    private const float Frame = 1f / 60f;

    [Fact]
    public void ANewRoundOnTheSameStepTracesItsOwnWins()
    {
        var cycle = new MachineTraceCycle();
        var threeLines = new[] { Line(0, 3, 400), Line(3, 4, 900), Line(7, 3, 100) };
        cycle.Track(1, 0, threeLines);
        cycle.Advance(MachineTraceCycle.SlotSeconds * 2.5f);
        Assert.Equal(3, cycle.Count);
        Assert.True(cycle.TryActive(threeLines, out _));

        var none = Array.Empty<CasinoSlotsWinDto>();
        cycle.Track(2, 0, none);
        Assert.Equal(0, cycle.Count);
        Assert.False(cycle.TryActive(none, out _));

        var oneLine = new[] { Line(5, 3, 250) };
        cycle.Track(3, 0, oneLine);
        Assert.Equal(1, cycle.Count);
        Assert.True(cycle.TryActive(oneLine, out var active));
        Assert.Equal(0, active);
    }

    [Fact]
    public void LinesCycleInPayOrder()
    {
        var cycle = new MachineTraceCycle();
        var wins = new[] { Line(0, 3, 100), Line(1, 5, 500), Line(2, 4, 200) };
        cycle.Track(1, 0, wins);
        var seen = new int[wins.Length];
        for (var slot = 0; slot < wins.Length; slot++)
        {
            Assert.True(cycle.TryActive(wins, out seen[slot]));
            cycle.Advance(MachineTraceCycle.SlotSeconds);
        }

        Assert.Equal(new[] { 1, 2, 0 }, seen);
    }

    [Fact]
    public void OnlyPaylinesAreTracedAsLines()
    {
        Assert.True(MachineTraceCycle.Traceable(Line(9, 3, 1)));
        Assert.False(MachineTraceCycle.Traceable(Line(SlotsRules.LineScatter, 3, 1)));
        Assert.False(MachineTraceCycle.Traceable(Line(SlotsRules.LineCluster, 9, 1)));
        Assert.False(MachineTraceCycle.Traceable(Line(SlotsRules.LineExpander, 3, 1)));
        Assert.False(MachineTraceCycle.Traceable(Line(SlotsRules.LineGrand, 15, 1)));
        Assert.False(MachineTraceCycle.Traceable(Line(SlotsRules.PaylineCount, 3, 1)));
    }

    [Fact]
    public void BackToBackRoundsNeverPointPastTheShownWins()
    {
        var playback = new MachineRoundPlayback();
        var cycle = new MachineTraceCycle();
        var traced = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var (name, round) in SlotsVectorFiles.Rounds())
            {
                var spin = SlotsVectorFiles.Spin(name, round);
                Assert.True(playback.Begin(spin), spin.RoundId);
                var guard = 0;
                while (guard < 200_000)
                {
                    playback.Update(Frame);
                    guard++;
                    if (!MachineReels.Presenting(playback))
                    {
                        continue;
                    }

                    var wins = playback.Current.Wins;
                    cycle.Track(playback.Serial, playback.StepIndex, wins);
                    cycle.Advance(Frame);
                    if (cycle.TryActive(wins, out var active))
                    {
                        Assert.InRange(active, 0, wins!.Length - 1);
                        Assert.True(MachineTraceCycle.Traceable(wins[active]), spin.RoundId);
                        traced++;
                    }

                    if (playback.Finished)
                    {
                        break;
                    }
                }
            }
        }

        Assert.True(traced > 0);
    }

    [Fact]
    public void EveryLineWinOfAStepIsShownOnce()
    {
        var spin = FirstLineWin();
        var step = 0;
        while (spin.Steps![step].Wins is not { Length: > 0 } stepWins || !HasLine(stepWins))
        {
            step++;
        }

        var wins = spin.Steps[step].Wins!;
        var cycle = new MachineTraceCycle();
        cycle.Track(1, step, wins);
        var shown = new bool[wins.Length];
        for (var slot = 0; slot < cycle.Count; slot++)
        {
            Assert.True(cycle.TryActive(wins, out var active));
            Assert.False(shown[active]);
            shown[active] = true;
            cycle.Advance(MachineTraceCycle.SlotSeconds);
        }

        for (var index = 0; index < wins.Length; index++)
        {
            Assert.Equal(MachineTraceCycle.Traceable(wins[index]), shown[index]);
        }
    }

    private static CasinoSlotsWinDto Line(int line, int count, long pay) =>
        new(line, 1, count, Array.Empty<int>(), pay);

    private static bool HasLine(CasinoSlotsWinDto[] wins)
    {
        for (var index = 0; index < wins.Length; index++)
        {
            if (MachineTraceCycle.Traceable(wins[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static CasinoSlotsSpinDto FirstLineWin()
    {
        foreach (var (name, round) in SlotsVectorFiles.Rounds())
        {
            var spin = SlotsVectorFiles.Spin(name, round);
            for (var index = 0; index < spin.Steps!.Length; index++)
            {
                if (spin.Steps[index].Wins is { } wins && HasLine(wins))
                {
                    return spin;
                }
            }
        }

        throw new InvalidOperationException("no line win in the vectors");
    }
}
