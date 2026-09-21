using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Core.Tests.Story;

public class RestorationTests
{
    private static readonly StoryBalance Story = GameBalance.CreateDefault().Story;

    /// <summary>Stars a player can earn over the whole campaign (3 per stage).</summary>
    private static int CampaignStars => Story.TotalStages * 3;

    [Fact]
    public void RebuildingCrystalheim_CostsRoughlyTheWholeCampaign()
    {
        Assert.Equal(3000, CampaignStars);
        Assert.Equal(2500, Restoration.TotalCost);

        // The bug: the old 116-star plan was finished around stage 70. The content must now need most of the campaign.
        Assert.True(Restoration.TotalCost > CampaignStars * 3 / 4, "Restoration is exhausted far too early.");
        Assert.True(Restoration.TotalCost <= CampaignStars, "Restoration cannot need more stars than the campaign pays.");

        // A realistic 2.5 stars/stage player needs the full 1000 stages; a perfect player still needs over 830.
        Assert.InRange(Restoration.TotalCost * 10 / 25, 900, Story.TotalStages);
        Assert.True(Restoration.TotalCost / 3 > 800);
    }

    [Fact]
    public void FirstTasksStayCheapEnoughToHookANewPlayer()
    {
        RestorationZone first = Restoration.Zones[0];
        Assert.Equal(1, first.Tasks[0].Cost);
        // Three stages at one star each already change the Market Square.
        Assert.True(first.Tasks[0].Cost + first.Tasks[1].Cost + first.Tasks[2].Cost <= 6);
        // The first zone is a small fraction of the whole plan.
        Assert.True(first.TotalCost * 20 < Restoration.TotalCost);
    }

    [Fact]
    public void ZonesGrowSteadily_AndTasksAreOrderedByCost()
    {
        Assert.Equal(5, Restoration.Zones.Count);
        Assert.Equal(50, Restoration.TotalTasks);
        int previous = 0;
        foreach (RestorationZone zone in Restoration.Zones)
        {
            Assert.Equal(Restoration.TasksPerZone, zone.Tasks.Count);
            Assert.True(zone.TotalCost > previous * 3 / 2, $"Zone {zone.Number} must be a real step up.");
            previous = zone.TotalCost;
            for (int i = 1; i < zone.Tasks.Count; i++)
            {
                Assert.True(zone.Tasks[i].Cost >= zone.Tasks[i - 1].Cost, $"{zone.Tasks[i].Id} is cheaper than the task before it.");
            }
        }
        Assert.Equal(1180, Restoration.Zones[4].TotalCost);
    }

    [Fact]
    public void EveryZoneEndsWithLivingDecorations()
    {
        foreach (RestorationZone zone in Restoration.Zones)
        {
            // Tasks 1-6 uncover the painting, 7-10 make it move; the screen relies on that split.
            for (int i = 0; i < 6; i++)
            {
                Assert.Equal(RestorationEffect.Structure, zone.Tasks[i].Effect);
            }
            Assert.Equal(4, zone.Tasks.Skip(6).Select(t => t.Effect).Distinct().Count());
            Assert.DoesNotContain(RestorationEffect.Structure, zone.Tasks.Skip(6).Select(t => t.Effect));
            Assert.Equal(RestorationEffect.Radiance, zone.Tasks[9].Effect);
            Assert.All(zone.Tasks, t => Assert.StartsWith("kingdom.fx.", t.EffectKey));
        }
    }

    [Fact]
    public void Build_SpendsStars_ZoneByZone_AndPaysTheZoneReward()
    {
        var progress = new StoryProgress { TotalStars = 60 };
        Assert.Equal(60, Restoration.AvailableStars(progress));
        Assert.Equal(ErrorCode.StageLocked, Restoration.Build(progress, "z2.t1").Error);

        RestorationBuildResult last = null!;
        foreach (RestorationTask task in Restoration.Zones[0].Tasks)
        {
            OperationResult<RestorationBuildResult> result = Restoration.Build(progress, task.Id);
            Assert.True(result.Success, task.Id);
            last = result.Value;
        }
        Assert.Equal(0, Restoration.AvailableStars(progress));
        Assert.Equal(10, Restoration.BuiltCount(progress.Restoration));
        Assert.NotNull(last.CompletedZone);
        Assert.Equal(1, last.CompletedZone!.Number);
        Assert.Equal(2_000, last.ZoneReward.Coins);
        Assert.Equal(2, Restoration.CurrentZone(progress.Restoration)!.Number);
        Assert.False(Restoration.CanBuildSomething(progress));
    }

    [Fact]
    public void SpendingEveryCampaignStar_RebuildsEverything()
    {
        var progress = new StoryProgress { TotalStars = CampaignStars };
        foreach (RestorationTask task in Restoration.Zones.SelectMany(z => z.Tasks))
        {
            Assert.True(Restoration.Build(progress, task.Id).Success, task.Id);
        }
        Assert.Null(Restoration.CurrentZone(progress.Restoration));
        Assert.Equal(Restoration.TotalCost, progress.Restoration.StarsSpent);
        Assert.Equal(CampaignStars - Restoration.TotalCost, Restoration.AvailableStars(progress));
    }
}
