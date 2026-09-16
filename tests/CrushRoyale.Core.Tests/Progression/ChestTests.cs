using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Tests.Gameplay;

namespace CrushRoyale.Core.Tests.Progression;

public class ChestTests
{
    private const long T0 = 1_790_000_000_000;

    [Fact]
    public void Grant_FillsFreeSlots_ThenReportsFull()
    {
        var chests = new ChestSystem(new ChestState(), Fixtures.Balance);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(i, chests.Grant(ChestType.Wood, "pvp"));
        }
        Assert.Equal(-1, chests.Grant(ChestType.Gold, "pvp"));
    }

    [Fact]
    public void OneChestUnlocksAtATime_AndOpensWhenReady()
    {
        var chests = new ChestSystem(new ChestState(), Fixtures.Balance);
        chests.Grant(ChestType.Silver, "pvp");
        chests.Grant(ChestType.Wood, "pvp");

        Assert.Equal(ErrorCode.None, chests.StartUnlock(0, T0));
        Assert.Equal(ErrorCode.LimitReached, chests.StartUnlock(1, T0));
        Assert.Equal(ChestSlotStatus.Unlocking, chests.Status(0, T0 + 1000));
        Assert.Equal(48, chests.SkipCostOrbes(0, T0)); // 8 h = 48 started 10-minute blocks

        var rng = new DeterministicRandom(3);
        Assert.Equal(ErrorCode.NotEnoughOrbes, chests.Open(0, T0 + 1000, 0, rng, League.Bronze).Error);

        long ready = T0 + 8 * 3600 * 1000L;
        Assert.Equal(ChestSlotStatus.Ready, chests.Status(0, ready));
        OperationResult<ChestContent> opened = chests.Open(0, ready, 0, rng, League.Bronze);
        Assert.True(opened.Success);
        Assert.InRange(opened.Value.Reward.Coins, 100, 180);
        Assert.Equal(2, opened.Value.Reward.PowerUps.Values.Sum());
        Assert.NotEqual(PetType.None, opened.Value.FragmentsPet);
        Assert.InRange(opened.Value.PetFragments, 5, 10);
        Assert.Equal(ChestSlotStatus.Empty, chests.Status(0, ready));

        // The slot is free again and the next chest can start.
        Assert.Equal(ErrorCode.None, chests.StartUnlock(1, ready));
    }

    [Fact]
    public void VictoryChests_AreMostlyWood_SometimesCrystal()
    {
        var chests = new ChestSystem(new ChestState(), Fixtures.Balance);
        var rng = new DeterministicRandom(11);
        var counts = new int[4];
        for (int i = 0; i < 10000; i++)
        {
            counts[(int)chests.RollVictoryChest(rng)]++;
        }
        Assert.InRange(counts[0], 5500, 6500);
        Assert.InRange(counts[3], 100, 320);
    }
}
