using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using Xunit;

namespace CrushRoyale.Core.Tests.Economy;

/// <summary>
/// The jar fills with what the player earns and is opened with real money. The rules that keep it honest: it never
/// swallows more than it can hold, it never pays out more than went in, and it is not offered while it is nearly
/// empty.
/// </summary>
public sealed class PiggyBankTests
{
    private static readonly PiggyBankBalance Balance = GameBalance.CreateDefault().Economy.PiggyBank;

    [Fact]
    public void ItFillsWithWhatThePlayerEarns()
    {
        var jar = new PiggyBankState();
        Assert.Equal(20, PiggyBank.Fill(jar, 20, Balance));
        Assert.Equal(20, jar.Orbes);
    }

    [Fact]
    public void ItStopsAtTheCapInsteadOfSwallowingForever()
    {
        var jar = new PiggyBankState { Orbes = Balance.CapOrbes - 5 };
        Assert.Equal(5, PiggyBank.Fill(jar, 100, Balance));
        Assert.Equal(Balance.CapOrbes, jar.Orbes);
        Assert.True(PiggyBank.IsFull(jar, Balance));
        Assert.Equal(0, PiggyBank.Fill(jar, 50, Balance));
    }

    [Fact]
    public void BreakingItHandsBackExactlyWhatWentIn()
    {
        var jar = new PiggyBankState();
        PiggyBank.Fill(jar, 250, Balance);
        Assert.Equal(250, PiggyBank.Break(jar));
        Assert.Equal(0, jar.Orbes);
        Assert.Equal(1, jar.TimesBroken);
    }

    [Fact]
    public void BreakingAnEmptyJarPaysNothing()
    {
        var jar = new PiggyBankState();
        Assert.Equal(0, PiggyBank.Break(jar));
        Assert.Equal(0, jar.TimesBroken);
    }

    /// <summary>An almost empty jar on the shelf teaches the player to ignore the offer.</summary>
    [Fact]
    public void ItIsNotOfferedUntilItHoldsSomethingWorthBuying()
    {
        var jar = new PiggyBankState { Orbes = Balance.MinOrbesToOffer - 1 };
        Assert.False(PiggyBank.CanOffer(jar, Balance));

        jar.Orbes = Balance.MinOrbesToOffer;
        Assert.True(PiggyBank.CanOffer(jar, Balance));
    }

    [Fact]
    public void ItKeepsFillingAfterItHasBeenBroken()
    {
        var jar = new PiggyBankState();
        PiggyBank.Fill(jar, 300, Balance);
        PiggyBank.Break(jar);
        Assert.Equal(40, PiggyBank.Fill(jar, 40, Balance));
        Assert.Equal(40, jar.Orbes);
    }
}
