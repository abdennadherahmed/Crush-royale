using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Progression;
using Xunit;

namespace CrushRoyale.Core.Tests.Progression;

/// <summary>
/// The two things that make a chest system feel fair rather than random: a floor under bad luck, and a chest that
/// arrives on its own clock so there is always a reason to open the game.
/// </summary>
public sealed class ChestCycleTests
{
    private static readonly GameBalance Balance = GameBalance.CreateDefault();

    private static ChestSystem System() => new(new ChestState(), Balance);

    [Fact]
    public void AGoldChestNeverTakesLongerThanThePromisedCycle()
    {
        ChestSystem chests = System();
        var rng = new DeterministicRandom(1234);
        int sinceGold = 0;

        for (int win = 0; win < 500; win++)
        {
            ChestType type = chests.GrantVictoryChest(rng);
            sinceGold = type == ChestType.Gold || type == ChestType.Crystal ? 0 : sinceGold + 1;
            Assert.True(sinceGold < Balance.Chests.GoldAtLatest,
                $"Win {win}: {sinceGold} chests without gold, the cycle promises one every {Balance.Chests.GoldAtLatest}.");
        }
    }

    [Fact]
    public void ACrystalChestAlsoArrivesWithinItsCycle()
    {
        ChestSystem chests = System();
        var rng = new DeterministicRandom(99);
        int sinceCrystal = 0;

        for (int win = 0; win < 500; win++)
        {
            ChestType type = chests.GrantVictoryChest(rng);
            sinceCrystal = type == ChestType.Crystal ? 0 : sinceCrystal + 1;
            Assert.True(sinceCrystal < Balance.Chests.CrystalAtLatest,
                $"Win {win}: {sinceCrystal} chests without crystal.");
        }
    }

    /// <summary>The counter is what the hub shows: it has to say the truth or it is worse than saying nothing.</summary>
    [Fact]
    public void TheCounterCountsDownAndResetsOnTheChestItPromised()
    {
        ChestSystem chests = System();
        var rng = new DeterministicRandom(7);
        int before = chests.WinsToGold();
        Assert.Equal(Balance.Chests.GoldAtLatest - 1, before);

        ChestType first = chests.GrantVictoryChest(rng);
        if (first == ChestType.Gold || first == ChestType.Crystal)
        {
            Assert.Equal(Balance.Chests.GoldAtLatest - 1, chests.WinsToGold());
        }
        else
        {
            Assert.Equal(before - 1, chests.WinsToGold());
        }
    }

    [Fact]
    public void RollingDoesNotMoveTheCycle_SoAPreviewCannotChangeWhatYouGet()
    {
        ChestSystem chests = System();
        var rng = new DeterministicRandom(5);
        int before = chests.WinsToGold();
        chests.RollVictoryChest(rng);
        chests.RollVictoryChest(rng);
        Assert.Equal(before, chests.WinsToGold());
    }

    [Fact]
    public void TheFreeChestIsReadyOnTheVeryFirstSession()
    {
        ChestSystem chests = System();
        Assert.True(chests.FreeChestReady(1_000_000));
        Assert.Equal(0, chests.FreeChestSecondsLeft(1_000_000));
    }

    [Fact]
    public void TakingTheFreeChestStartsItsOwnClock()
    {
        ChestSystem chests = System();
        long now = 1_000_000_000;

        OperationResult<int> taken = chests.TakeFreeChest(now);
        Assert.True(taken.Success);
        Assert.False(chests.FreeChestReady(now));
        Assert.Equal(Balance.Chests.FreeChestSeconds, chests.FreeChestSecondsLeft(now));

        Assert.False(chests.TakeFreeChest(now + 1000).Success);
        Assert.True(chests.FreeChestReady(now + (long)Balance.Chests.FreeChestSeconds * 1000));
    }

    /// <summary>Tapping it with no room must not burn the timer: nothing is lost by being early.</summary>
    [Fact]
    public void AFullChestBarDoesNotConsumeTheFreeChest()
    {
        ChestSystem chests = System();
        long now = 2_000_000_000;
        for (int i = 0; i < Balance.Chests.Slots; i++)
        {
            chests.Grant(ChestType.Wood, "test");
        }

        Assert.False(chests.TakeFreeChest(now).Success);
        Assert.True(chests.FreeChestReady(now));
    }
}
