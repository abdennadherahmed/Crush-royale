using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Progression;

namespace CrushRoyale.Core.Tests.Progression;

public class SeasonCatalogTests
{
    private static LiveOpsBalance Live => GameBalance.CreateDefault().LiveOps;

    [Fact]
    public void BetaComesFirst_AndLaunchDayIsSeasonOne()
    {
        SeasonDefinition beta = SeasonCatalog.All[0];
        Assert.Equal("beta", beta.Id);
        Assert.Equal(0, beta.Index);
        Assert.True(beta.IsBeta);
        Assert.Equal(13, SeasonCatalog.All.Count);

        // Before and during the beta window the pass is the beta season, never a number.
        Assert.Equal(0, SeasonCatalog.IndexAt(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(0, SeasonCatalog.IndexAt(SeasonCatalog.BetaStartUtc));
        Assert.Equal(0, SeasonCatalog.IndexAt(SeasonCatalog.LaunchUtc.AddSeconds(-1)));

        // Launch day flips to season 1 exactly.
        Assert.Equal(1, SeasonCatalog.IndexAt(SeasonCatalog.LaunchUtc));
        Assert.Equal("s1", SeasonCatalog.At(SeasonCatalog.LaunchUtc).Id);
        Assert.Equal(1, SeasonCatalog.IndexAt(SeasonCatalog.LaunchUtc.AddDays(27.9)));
        Assert.Equal(2, SeasonCatalog.IndexAt(SeasonCatalog.LaunchUtc.AddDays(28)));
        Assert.Equal(12, SeasonCatalog.IndexAt(SeasonCatalog.LaunchUtc.AddDays(11 * 28)));
    }

    [Fact]
    public void NoNonsenseSeasonNumberEver()
    {
        // The bug: the index was days-since-epoch / 28, so "today" advertised season 739.
        for (DateTime day = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc); day.Year < 2040; day = day.AddDays(7))
        {
            int index = BattlePass.SeasonIndex(day, Live);
            Assert.True(index >= 0, $"{day:O} -> {index}");
            Assert.NotEqual(739, index);
            // 13 authored seasons plus one 28-day encore per cycle: far below the epoch-derived numbers.
            int max = day < SeasonCatalog.Last.EndUtc ? 12 : 13 + (int)((day - SeasonCatalog.Last.EndUtc).TotalDays / 28);
            Assert.True(index <= max, $"{day:O} -> {index}");
        }
    }

    [Fact]
    public void SeasonsAreContiguous_AndThemedOnTheFiveKingdoms()
    {
        for (int i = 1; i < SeasonCatalog.All.Count; i++)
        {
            Assert.Equal(SeasonCatalog.All[i - 1].EndUtc, SeasonCatalog.All[i].StartUtc);
            Assert.Equal(i, SeasonCatalog.All[i].Index);
            Assert.Equal(28, SeasonCatalog.All[i].DurationDays);
        }
        Assert.Equal(5, SeasonCatalog.All.Select(s => s.Theme).Distinct().Count());
    }

    [Fact]
    public void ExclusiveCosmetics_AreUnique_Registered_AndOnlyOnThePremiumTrack()
    {
        var ids = SeasonCatalog.All.Select(s => s.ExclusiveCosmeticId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (SeasonDefinition season in SeasonCatalog.All)
        {
            CosmeticDefinition def = CosmeticCatalog.Get(season.ExclusiveCosmeticId);
            Assert.NotNull(def);
            Assert.Equal(CosmeticKind.HeroOutfit, def!.Kind);
            Assert.Equal(CosmeticSource.BattlePass, def.Source);
            // Exclusive means exclusive: not buyable, not a VIP gift.
            Assert.Equal(0, def.PriceOrbes);
            Assert.Equal(0, def.RequiredVip);

            BattlePassTierReward top = BattlePass.GetTierReward(season.Index, 50);
            Assert.Contains(season.ExclusiveCosmeticId, top.Premium.Cosmetics);
            Assert.DoesNotContain(season.ExclusiveCosmeticId, top.Free.Cosmetics);
        }
    }

    [Fact]
    public void AfterSeasonTwelve_TheCatalogLoopsWithNewIdsAndRisingNumbers()
    {
        DateTime afterLast = SeasonCatalog.Last.EndUtc;
        SeasonDefinition first = SeasonCatalog.At(afterLast);
        Assert.Equal(13, first.Index);
        Assert.Equal("s13", first.Id);
        Assert.Equal(afterLast, first.StartUtc);
        Assert.Equal(28, first.DurationDays);
        Assert.Equal("season.encore", first.NameKey);
        Assert.NotNull(CosmeticCatalog.Get(first.ExclusiveCosmeticId));

        SeasonDefinition later = SeasonCatalog.At(afterLast.AddDays(28 * 5 + 3));
        Assert.Equal(18, later.Index);
        Assert.Equal("s18", later.Id);
        Assert.NotEqual(first.Id, later.Id);

        // ByIndex rebuilds the same encore season the date lookup returns (client and server must agree).
        Assert.Equal(later.Id, SeasonCatalog.ByIndex(18).Id);
        Assert.Equal(later.StartUtc, SeasonCatalog.ByIndex(18).StartUtc);
        Assert.Equal(later.ExclusiveCosmeticId, SeasonCatalog.ByIndex(18).ExclusiveCosmeticId);
    }

    [Fact]
    public void SeasonEnd_MatchesTheCatalog_AndResetsTheBattlePass()
    {
        Assert.Equal(SeasonCatalog.LaunchUtc, BattlePass.SeasonEndUtc(0, Live));
        Assert.Equal(SeasonCatalog.LaunchUtc.AddDays(28), BattlePass.SeasonEndUtc(1, Live));

        var state = new BattlePassState();
        Assert.True(BattlePass.EnsureSeason(state, SeasonCatalog.LaunchUtc.AddDays(-1), Live));
        Assert.Equal(0, state.SeasonIndex);
        BattlePass.AddXp(state, 5000);
        Assert.True(BattlePass.EnsureSeason(state, SeasonCatalog.LaunchUtc, Live));
        Assert.Equal(1, state.SeasonIndex);
        Assert.Equal(0, state.Xp);
    }
}
