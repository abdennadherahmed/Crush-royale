using System.Linq;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Story;
using CrushRoyale.Core.Tests.Economy;
using CrushRoyale.Core.Tests.Gameplay;
using Xunit;

namespace CrushRoyale.Core.Tests.Story;

/// <summary>Stage kinds (timed / moves), the difficulty sawtooth, win-streak starting bonuses and the daily wheel.</summary>
public sealed class EngagementTests
{
    private static readonly GameBalance Balance = GameBalance.CreateDefault();

    [Fact]
    public void Catalog_SplitsTimedAndMovesStages_WithASawtooth()
    {
        var catalog = new StageCatalog(Balance);
        int timed = 0, hard = 0, superHard = 0;
        for (int id = 1; id <= 200; id++)
        {
            StageData stage = catalog.Get(id);
            if (stage.Timed)
            {
                timed++;
                Assert.Equal(0, stage.MoveLimit);
                Assert.False(stage.IsBoss);
                Assert.InRange(stage.TimeLimitMs, Balance.Story.TimedHardTimeLimitMs, Balance.Story.TimedEasyTimeLimitMs);
            }
            else
            {
                Assert.True(stage.MoveLimit > 0);
                Assert.Equal(Balance.Story.MovesStageTimeCapMs, stage.TimeLimitMs);
            }
            hard += stage.Tier == StageTier.Hard ? 1 : 0;
            superHard += stage.Tier == StageTier.SuperHard ? 1 : 0;
        }
        Assert.InRange(timed, 30, 45);
        Assert.Equal(StageTier.Normal, catalog.Get(7).Tier);
        Assert.Equal(StageTier.Hard, catalog.Get(14).Tier);
        Assert.Equal(StageTier.SuperHard, catalog.Get(17).Tier);
        Assert.True(hard >= 18 && superHard >= 9);
        Assert.True(catalog.Get(37).RewardCoins > catalog.Get(36).RewardCoins);
        Assert.Equal(Balance.Story.SuperHardOrbes, catalog.Get(37).RewardOrbes);
    }

    [Fact]
    public void MovesStage_HasNoTimeWarning_AndNoTimeBonus()
    {
        var session = Fixtures.StorySession(Fixtures.Stage(moves: 20, timeMs: 900000, target: 200));
        int warnings = 0;
        session.OnTimeWarning += _ => warnings++;
        session.Tick(895000);
        Assert.Equal(0, warnings);
    }

    [Fact]
    public void TimedStage_AssistGivesTimeInsteadOfMoves()
    {
        SessionConfig config = SessionConfig.ForStage(Fixtures.Stage(moves: 0, timeMs: 60000), Balance, null, League.Bronze, assistExtraMoves: 4);
        var session = new GameSession(config, Balance, "p");
        Assert.Equal(60000 + 4 * GameSession.AssistMsPerMove, session.TimeLimitMs);
        Assert.Equal(0, session.MovesLeft);
    }

    [Fact]
    public void WinStreak_GrowsOnNewWins_ResetsOnLoss_AndGivesBoosters()
    {
        ManualClock clock = EconomyFixtures.Clock();
        var story = new StoryManager(Balance, new StageCatalog(Balance), new StoryProgress(), clock);
        for (int stage = 1; stage <= 5; stage++)
        {
            Assert.True(story.CompleteStage(stage, StageStatus.Won, 1000, 3).Accepted);
        }
        Assert.Equal(5, story.Progress.WinStreak);
        Assert.Equal(2, story.GetStreakBoosters(6));
        Assert.Equal(0, story.GetStreakBoosters(3)); // already won: no bonus

        // Replaying an old stage neither grows nor breaks the streak.
        story.CompleteStage(2, StageStatus.Lost, 0, 0);
        Assert.Equal(5, story.Progress.WinStreak);

        StageCompletion loss = story.CompleteStage(6, StageStatus.Lost, 0, 0);
        Assert.Equal(5, loss.StreakLost);
        Assert.Equal(0, story.Progress.WinStreak);
        Assert.Equal(5, story.Progress.BestWinStreak);
        Assert.Equal(0, story.GetStreakBoosters(6));
    }

    [Fact]
    public void StartBoosters_AreSpecialGems_IdenticalOnReplay()
    {
        SessionConfig config = SessionConfig.ForStage(new StageCatalog(Balance).Get(30), Balance, null, League.Bronze).WithStartBoosters(3);
        var a = new GameSession(config, Balance, "p");
        var b = new GameSession(config, Balance, "p");
        Assert.Equal(a.InitialBoardHash, b.InitialBoardHash);
        Assert.Equal(3, a.Board.AllPositions().Count(p => a.Board[p].IsSpecial));

        var plain = new GameSession(SessionConfig.ForStage(new StageCatalog(Balance).Get(30), Balance, null, League.Bronze), Balance, "p");
        Assert.NotEqual(plain.InitialBoardHash, a.InitialBoardHash);

        a.Abandon(1000);
        ReplayData round = ReplaySerializer.Deserialize(ReplaySerializer.Serialize(a.Replay));
        Assert.Equal(3, round.StartBoosters);
    }

    [Fact]
    public void DailyWheel_OnceADay_DeterministicPerPlayerAndDay()
    {
        Assert.True(DailyWheel.CanSpin(-1, 100));
        Assert.False(DailyWheel.CanSpin(100, 100));
        Assert.Equal(DailyWheel.Spin("player", 42).SliceIndex, DailyWheel.Spin("player", 42).SliceIndex);

        var hits = new int[DailyWheel.Slices.Count];
        for (int day = 0; day < 4000; day++)
        {
            WheelSpin spin = DailyWheel.Spin("p" + (day % 7), day);
            hits[spin.SliceIndex]++;
            Assert.True(!spin.Reward.IsEmpty || spin.PetFragments > 0);
        }
        Assert.All(hits, h => Assert.True(h > 0));
        Assert.True(hits[0] > hits[DailyWheel.Slices.Count - 1] * 4);
    }
}
