using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Progression;
using Xunit;

namespace CrushRoyale.Core.Tests.Progression;

/// <summary>
/// A window of free play: the lever Royal Match uses to turn a bad evening into a long session. While it runs the
/// lives wall does not exist, and nothing about it may quietly eat a life.
/// </summary>
public sealed class UnlimitedLivesTests
{
    private static readonly GameBalance Balance = GameBalance.CreateDefault();

    private static (StaminaManager Stamina, ManualClock Clock) Fresh()
    {
        var clock = new ManualClock(new System.DateTime(2026, 9, 22, 12, 0, 0, System.DateTimeKind.Utc));
        return (new StaminaManager(Balance, new StaminaState(), clock), clock);
    }

    [Fact]
    public void LosingCostsNothingWhileTheWindowRuns()
    {
        (StaminaManager stamina, _) = Fresh();
        int before = stamina.Lives;
        stamina.GrantUnlimited(60);

        for (int i = 0; i < 20; i++)
        {
            Assert.True(stamina.ConsumeLive());
        }
        Assert.Equal(before, stamina.Lives);
    }

    [Fact]
    public void WinningDoesNotHandBackALifeThatWasNeverTaken()
    {
        (StaminaManager stamina, _) = Fresh();
        stamina.GrantUnlimited(30);
        int before = stamina.Lives;

        stamina.ConsumeLive();
        stamina.RefundLife();
        Assert.Equal(before, stamina.Lives);
    }

    [Fact]
    public void TheWallComesBackWhenTheWindowEnds()
    {
        (StaminaManager stamina, ManualClock clock) = Fresh();
        stamina.GrantUnlimited(10);
        Assert.True(stamina.Unlimited);

        clock.Advance(System.TimeSpan.FromMinutes(11));
        Assert.False(stamina.Unlimited);
        Assert.Equal(0, stamina.UnlimitedSecondsLeft());
    }

    /// <summary>Two rewards on the same evening must add up, not silently replace one another.</summary>
    [Fact]
    public void TwoGrantsAddUpInsteadOfOneEatingTheOther()
    {
        (StaminaManager stamina, _) = Fresh();
        stamina.GrantUnlimited(30);
        stamina.GrantUnlimited(30);
        Assert.InRange(stamina.UnlimitedSecondsLeft(), 3500, 3600);
    }

    [Fact]
    public void NoWindowMeansTheNormalRules()
    {
        (StaminaManager stamina, _) = Fresh();
        Assert.False(stamina.Unlimited);
        int before = stamina.Lives;
        Assert.True(stamina.ConsumeLive());
        Assert.Equal(before - 1, stamina.Lives);
    }
}
