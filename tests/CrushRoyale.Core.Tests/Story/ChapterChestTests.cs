using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;
using CrushRoyale.Core.Tests.Gameplay;

namespace CrushRoyale.Core.Tests.Story;

public class ChapterChestTests
{
    private static StoryBalance Story => Fixtures.Balance.Story;

    private static StoryProgress WithStars(int chapter, int stagesWon, int starsEach)
    {
        var progress = new StoryProgress();
        int first = (chapter - 1) * Story.StagesPerChapter + 1;
        for (int i = 0; i < stagesWon; i++)
        {
            progress.Stages[first + i] = new StageProgress { StageId = first + i, EverWon = true, BestStars = starsEach };
        }
        return progress;
    }

    [Fact]
    public void Thresholds_Are30_45_60_ForTwentyStages()
    {
        Assert.Equal(20, Story.StagesPerChapter);
        Assert.Equal(30, ChapterChests.StarsNeeded(Story, 0));
        Assert.Equal(45, ChapterChests.StarsNeeded(Story, 1));
        Assert.Equal(60, ChapterChests.StarsNeeded(Story, 2));
    }

    [Fact]
    public void Claim_NeedsTheStars_AndOnlyOnce()
    {
        StoryProgress progress = WithStars(chapter: 2, stagesWon: 15, starsEach: 2); // 30 stars
        Assert.Equal(30, ChapterChests.StarsIn(progress, Story, 2));
        Assert.Equal(0, ChapterChests.StarsIn(progress, Story, 1));

        Assert.Equal(ErrorCode.LimitReached, ChapterChests.Claim(progress, Story, 2, 1).Error);
        OperationResult<ChapterChestReward> first = ChapterChests.Claim(progress, Story, 2, 0);
        Assert.True(first.Success);
        Assert.True(first.Value.Reward.Coins > 0);
        Assert.Equal(ErrorCode.AlreadyClaimed, ChapterChests.Claim(progress, Story, 2, 0).Error);
        Assert.Equal(ErrorCode.InvalidArgument, ChapterChests.Claim(progress, Story, 2, 3).Error);
    }

    [Fact]
    public void Rewards_GrowWithTierAndChapter_AndTheLastGivesPetFragments()
    {
        ChapterChestReward low = ChapterChests.RewardFor(1, 0);
        ChapterChestReward high = ChapterChests.RewardFor(1, 2);
        ChapterChestReward later = ChapterChests.RewardFor(20, 2);
        Assert.True(high.Reward.Coins > low.Reward.Coins);
        Assert.True(high.Reward.Orbes > low.Reward.Orbes);
        Assert.True(later.Reward.Coins > high.Reward.Coins);
        Assert.Equal(0, low.PetFragments);
        Assert.True(high.PetFragments > 0);
        Assert.NotEqual(PetType.None, high.FragmentsPet);
    }
}
