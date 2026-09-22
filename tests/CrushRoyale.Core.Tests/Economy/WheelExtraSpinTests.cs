using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using Xunit;

namespace CrushRoyale.Core.Tests.Economy;

/// <summary>The free spin stays free, and the paid ones climb steeply enough to stay a choice.</summary>
public sealed class WheelExtraSpinTests
{
    private static readonly WheelBalance Balance = GameBalance.CreateDefault().Economy.Wheel;

    [Fact]
    public void TheFirstPaidSpinCostsTheBasePrice()
    {
        Assert.Equal(Balance.ExtraSpinBaseOrbes, DailyWheel.ExtraSpinCost(0, Balance));
    }

    [Fact]
    public void EachPaidSpinCostsMoreThanTheLast()
    {
        int previous = 0;
        for (int i = 0; i < Balance.ExtraSpinsPerDay; i++)
        {
            int price = DailyWheel.ExtraSpinCost(i, Balance);
            Assert.True(price > previous, $"Spin {i} costs {price}, the one before cost {previous}.");
            previous = price;
        }
    }

    [Fact]
    public void ThereIsAFloorOnHowManyCanBeBoughtInADay()
    {
        Assert.Equal(0, DailyWheel.ExtraSpinCost(Balance.ExtraSpinsPerDay, Balance));
        Assert.Equal(0, DailyWheel.ExtraSpinCost(Balance.ExtraSpinsPerDay + 5, Balance));
    }

    /// <summary>Paying twice must not land on the same slice, or the second purchase is a swindle.</summary>
    [Fact]
    public void TwoPaidSpinsAreRolledIndependently()
    {
        int same = 0;
        for (int day = 0; day < 40; day++)
        {
            WheelSpin first = DailyWheel.SpinExtra("player-1", day, 0);
            WheelSpin second = DailyWheel.SpinExtra("player-1", day, 1);
            if (first.SliceIndex == second.SliceIndex)
            {
                same++;
            }
        }
        Assert.True(same < 40, "Both paid spins always landed on the same slice.");
    }

    [Fact]
    public void ThePaidSpinIsStillTheSameWheel()
    {
        WheelSpin spin = DailyWheel.SpinExtra("player-2", 10, 0);
        Assert.InRange(spin.SliceIndex, 0, DailyWheel.Slices.Count - 1);
    }
}
