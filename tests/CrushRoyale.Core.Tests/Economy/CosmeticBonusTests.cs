using CrushRoyale.Core.Economy;

namespace CrushRoyale.Core.Tests.Economy;

public class CosmeticBonusTests
{
    [Fact]
    public void Bonuses_Stack_ByRarity_IgnoringDefaults()
    {
        var owned = new[] { "board.classic", "pieces.classic", "emote.gg", "frame.bronze", "frame.silver", "frame.gold", "frame.crown" };
        (int coins, int xp, int counted) = CosmeticBonuses.Total(owned);
        Assert.Equal(4, counted);
        Assert.Equal(5 + 10 + 20 + 40, coins);
        Assert.Equal(0 + 5 + 10 + 20, xp);
    }

    [Fact]
    public void Bonuses_AreCapped()
    {
        var many = new List<string>();
        foreach (CosmeticDefinition c in CosmeticCatalog.All)
        {
            many.Add(c.Id);
        }
        for (int season = 0; season < 40; season++)
        {
            many.Add(CosmeticCatalog.BattlePassPrefix + season + ".frame");
        }
        (int coins, int xp, _) = CosmeticBonuses.Total(many);
        Assert.Equal(CosmeticBonuses.MaxCoinPermille, coins);
        Assert.Equal(CosmeticBonuses.MaxPassXpPermille, xp);
    }
}
