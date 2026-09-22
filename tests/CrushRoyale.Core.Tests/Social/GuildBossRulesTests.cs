using System.Linq;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Social;
using Xunit;

namespace CrushRoyale.Core.Tests.Social;

/// <summary>
/// The three guild bosses have to fight differently.
///
/// They used to share one board and one set of rules and differ only by portrait, so a week against one was
/// indistinguishable from a week against another. These tests pin what each of them actually does to the board.
/// </summary>
public sealed class GuildBossRulesTests
{
    private static readonly GameBalance Balance = GameBalance.CreateDefault();

    private static SessionConfig ConfigFor(string bossId)
    {
        int index = GuildBossRoster.All.ToList().FindIndex(b => b.Id == bossId) + 1;
        return SessionConfig.ForGuildBoss(1234, index, Balance, null, League.Gold);
    }

    [Fact]
    public void EveryBoss_HasRulesOfItsOwn()
    {
        Assert.Equal(3, GuildBossRoster.All.Count);
        foreach (GuildBossIdentity boss in GuildBossRoster.All)
        {
            GuildBossRules r = boss.Rules;
            bool fights = r.Bombs > 0 || r.BlightLossPermille > 0 || r.IceLossPermille > 0;
            Assert.True(fights, boss.Name + " has no mechanic of its own.");
        }
    }

    [Fact]
    public void MajorsBlue_PlantsBombs_AndNothingElse()
    {
        SessionConfig config = ConfigFor("majors_blue");
        Assert.True(config.TimeBombCount > 0);
        Assert.True(config.TimeBombMoves > 0);
        Assert.Equal(0, config.Board.BlightCount);
        Assert.Equal(0, config.Board.IceCells);
        Assert.Equal(0, config.IceLossPermille);
    }

    [Fact]
    public void SevenKou_StartsCorrupted_AndLosesAtHalfTheBoard()
    {
        SessionConfig config = ConfigFor("7kou");
        int cells = config.Board.Width * config.Board.Height;
        Assert.Equal(cells / 4, config.Board.BlightCount);
        // The generator blocks at most a quarter of the board across stones, blight and eggs, so this boss spends
        // the whole allowance on corruption and brings no stones.
        Assert.Equal(0, config.Board.StoneCount);
        Assert.Equal(500, config.BlightLossPermille);
        // The board must start well clear of the threshold, otherwise the attack is lost before a move is played.
        Assert.True(config.Board.BlightCount * 1000 / cells < config.BlightLossPermille);
        Assert.Equal(0, config.TimeBombCount);
    }

    [Fact]
    public void Escobaros_FreezesTheBoard_InTwoLayers_AndGrowsOnAWastedMove()
    {
        SessionConfig config = ConfigFor("escobaros");
        int cells = config.Board.Width * config.Board.Height;
        Assert.Equal(cells / 4, config.Board.IceCells);
        // Two layers is the "two moves" rule: one to crack the ice, one to clear what it covers.
        Assert.Equal(2, config.Board.IceLayers);
        Assert.Equal(750, config.IceLossPermille);
        Assert.True(config.IcePerWastedMove > 0);
        Assert.True(config.Board.IceCells * 1000 / cells < config.IceLossPermille);
        Assert.Equal(0, config.TimeBombCount);
    }

    [Fact]
    public void TheRoster_RepeatsInOrder_SoAGuildMeetsTheSameThreeBosses()
    {
        Assert.Equal("7kou", GuildBossRoster.For(1).Id);
        Assert.Equal("escobaros", GuildBossRoster.For(2).Id);
        Assert.Equal("majors_blue", GuildBossRoster.For(3).Id);
        Assert.Equal("7kou", GuildBossRoster.For(4).Id);
    }
}
