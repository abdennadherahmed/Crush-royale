using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Story;
using CrushRoyale.Core.Tests.Gameplay;
using Xunit.Abstractions;

namespace CrushRoyale.Core.Tests.Story;

public class StageCatalogTests
{
    private readonly StageCatalog _catalog = new(Fixtures.Balance);

    [Fact]
    public void Campaign_Has1000Stages_WithGddStructure()
    {
        var stages = _catalog.GetRange(1, 1000).ToList();
        Assert.Equal(1000, _catalog.TotalCampaignStages);
        Assert.All(stages, s => Assert.NotEmpty(s.Objectives));
        Assert.Equal(50, stages.Count(s => s.BossKind == BossKind.MiniBoss));
        Assert.Equal(50, stages.Count(s => s.BossKind >= BossKind.ChapterBoss));
        Assert.Equal(5, stages.Count(s => s.BossKind >= BossKind.ActBoss));
        int milestone = Fixtures.Balance.Story.OrbeMilestoneReward;
        Assert.Equal(20, stages.Count(s => s.RewardOrbes == milestone));
        Assert.Equal(1000, stages.Select(s => s.Seed).Distinct().Count());
        Assert.True(stages.Select(s => s.Objectives[0].Type).Distinct().Count() >= 5);
    }

    [Theory]
    [InlineData(1, 1, 1, 1, BossKind.None)]
    [InlineData(10, 1, 1, 10, BossKind.MiniBoss)]
    [InlineData(20, 1, 1, 20, BossKind.ChapterBoss)]
    [InlineData(200, 1, 10, 20, BossKind.ActBoss)]
    [InlineData(201, 2, 11, 1, BossKind.None)]
    [InlineData(1000, 5, 50, 20, BossKind.FinalBoss)]
    public void Structure(int id, int act, int chapter, int index, BossKind kind)
    {
        var s = _catalog.Get(id);
        Assert.Equal(act, s.Act);
        Assert.Equal(chapter, s.Chapter);
        Assert.Equal(index, s.IndexInChapter);
        Assert.Equal(kind, s.BossKind);
        Assert.Equal((Kingdom)(act - 1), s.Kingdom);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(20, 120)]
    [InlineData(100, 400)]
    [InlineData(500, 800)]
    [InlineData(1000, 1000)]
    [InlineData(1500, 1000)]
    public void Difficulty_RampsFastEarly_ThenFlattens(int id, int permille)
    {
        Assert.Equal(permille, _catalog.Get(id).DifficultyPermille);
    }

    [Fact]
    public void EveryGoal_StaysWithinWhatTheExpertBotReaches()
    {
        // StageTuning.g.cs comes from tools/StageAudit --bake: re-bake when stage generation or scoring changes.
        Assert.Equal(Fixtures.Balance.Story.TotalStages + 1, StageTuning.Score.Length);
        for (int id = 1; id <= Fixtures.Balance.Story.TotalStages; id++)
        {
            StageData stage = _catalog.Get(id);
            foreach (StageObjective goal in stage.Objectives)
            {
                switch (goal.Type)
                {
                    case ObjectiveType.ReachScore:
                    case ObjectiveType.DefeatBoss:
                        Assert.True(goal.Target <= Math.Max(200, StageTuning.Score[id] * 9 / 10 + 50), $"stage {id}: {goal.Target} vs reach {StageTuning.Score[id]}");
                        break;
                    case ObjectiveType.ClearIce:
                        Assert.True(goal.Target <= Math.Max(1, StageTuning.Ice[id]), $"stage {id} ice");
                        break;
                    case ObjectiveType.BreakStones:
                        Assert.True(goal.Target <= Math.Max(1, StageTuning.Stones[id]), $"stage {id} stones");
                        break;
                }
            }
        }
    }

    [Fact]
    public void FinalBoss_IsValdorax_AndEndlessContinues()
    {
        Assert.Equal("boss.valdorax", _catalog.Get(1000).BossId);
        var endless = _catalog.Get(1001);
        Assert.True(endless.IsEndless);
        Assert.NotEqual(_catalog.Get(801).Seed, endless.Seed);
    }

    [Fact]
    public void EveryNthStage_BuildsAPlayableSession()
    {
        for (int id = 1; id <= 1000; id += 7)
        {
            var stage = _catalog.Get(id);
            var session = new GameSession(SessionConfig.ForStage(stage, Fixtures.Balance, null, League.Bronze), Fixtures.Balance);
            Assert.True(session.BoardManager.GetValidMoves().Count > 0, $"stage {id}");
            Assert.Equal(stage.StoneCount, session.Board.CountStones());
        }
    }
}

public class BalancingTests(ITestOutputHelper output)
{
    /// <summary>
    /// Automated balancing: a greedy bot (no planning, 0.9 s per move) must be able to beat the campaign
    /// often early on and still regularly late. Real players plan ahead and use power-ups/continues.
    /// </summary>
    [Fact]
    public void GreedyBot_WinRates_PerBand()
    {
        var catalog = new StageCatalog(Fixtures.Balance);
        var bands = new[] { (1, 50), (51, 200), (201, 500), (501, 800), (801, 1000) };
        var rates = new List<double>();
        foreach (var (from, to) in bands)
        {
            int wins = 0, total = 0;
            // Dense sampling: 25 stages per band was noisy enough to flip the comparison between bands.
            for (int id = from; id <= to; id += Math.Max(1, (to - from) / 60))
            {
                var stage = catalog.Get(id);
                var session = new GameSession(SessionConfig.ForStage(stage, Fixtures.Balance, null, League.Bronze), Fixtures.Balance);
                if (HeadlessRunner.Run(session, new GreedyBot()).Won)
                {
                    wins++;
                }
                total++;
            }
            double rate = wins / (double)total;
            rates.Add(rate);
            output.WriteLine($"stages {from}-{to}: greedy bot wins {wins}/{total} ({rate:P0})");
        }

        Assert.True(rates[0] >= 0.8, "Early stages must be easy.");
        Assert.True(rates[^1] >= 0.2, "Late stages must remain beatable.");
        Assert.True(rates[0] >= rates[^1], "Difficulty must increase.");
    }
}

public class StoryManagerTests
{
    private static StoryManager Manager(StoryProgress? progress = null) =>
        new(Fixtures.Balance, new StageCatalog(Fixtures.Balance), progress ?? new StoryProgress(), new ManualClock(new DateTime(2026, 9, 14)));

    private static StoryProgress ProgressAt(int highestUnlocked)
    {
        var p = new StoryProgress { HighestUnlockedStage = highestUnlocked };
        for (int i = 1; i < highestUnlocked; i++)
        {
            p.Stages[i] = new StageProgress { StageId = i, EverWon = true, LastStatus = StageStatus.Won, BestStars = 1 };
        }
        return p;
    }

    [Fact]
    public void Winning_UnlocksNext_AndPaysOnce()
    {
        var story = Manager();
        Assert.True(story.CanPlay(1));
        Assert.False(story.CanPlay(2));
        Assert.Equal(ErrorCode.StageLocked, story.TryLoadStage(2).Error);
        Assert.Throws<InvalidOperationException>(() => story.LoadStage(5));

        var first = story.CompleteStage(1, StageStatus.Won, 5000, 3);
        Assert.True(first.FirstWin);
        Assert.Equal(new StageCatalog(Fixtures.Balance).Get(1).RewardCoins, first.BaseCoins);
        Assert.Equal(2, story.CurrentStage);
        Assert.Equal(2, first.NextStage);
        Assert.Equal(3, story.Progress.TotalStars);

        var replay = story.CompleteStage(1, StageStatus.Won, 6000, 2);
        Assert.False(replay.FirstWin);
        Assert.Equal(first.BaseCoins / 4, replay.BaseCoins);
        Assert.Equal(3, story.Progress.TotalStars);
        Assert.Equal(0.1f, story.GetProgressionPercent(), 3);
    }

    [Fact]
    public void FeatureUnlocks_FollowGdd()
    {
        var story = Manager(ProgressAt(25));
        Assert.True(story.IsFeatureUnlocked(Feature.Shop));
        Assert.False(story.IsFeatureUnlocked(Feature.Pvp));
        var completion = story.CompleteStage(25, StageStatus.Won, 1, 1);
        Assert.Contains(Feature.Pvp, completion.FeaturesUnlocked);
        Assert.True(story.IsFeatureUnlocked(Feature.Pvp));
        Assert.True(story.CompleteStage(26, false, 10));
    }

    [Fact]
    public void DifficultyAssist_AfterRepeatedFailures()
    {
        var story = Manager(ProgressAt(30));
        for (int i = 0; i < 5; i++)
        {
            story.CompleteStage(30, StageStatus.Lost, 100, 0);
        }
        Assert.Equal(2, story.GetAssistExtraMoves(30));
        story.CompleteStage(30, StageStatus.Lost, 100, 0);
        Assert.Equal(4, story.GetAssistExtraMoves(30));
        story.CompleteStage(30, StageStatus.Won, 9999, 1);
        Assert.Equal(0, story.GetAssistExtraMoves(30));
    }

    [Fact]
    public void Events_CharacterJoins_AndSeenTracking()
    {
        var story = Manager(ProgressAt(49));
        var completion = story.CompleteStage(49, StageStatus.Won, 1, 1);
        Assert.Contains("lyra", completion.CharactersJoined);

        var before = story.GetPendingEvents(50, StoryEventTrigger.BeforeStage);
        Assert.Contains(before, e => e.CharacterJoins == "lyra");
        story.MarkEventSeen("lyra_joins");
        Assert.DoesNotContain(story.GetPendingEvents(50, StoryEventTrigger.BeforeStage), e => e.CharacterJoins == "lyra");
    }

    [Fact]
    public void Party_ReflectsDeathsBetrayalAndChoices()
    {
        var story = Manager(ProgressAt(761));
        var party = story.GetParty().Select(c => c.Id).ToList();
        Assert.DoesNotContain("thorin", party);
        Assert.DoesNotContain("mira", party);
        Assert.DoesNotContain("mark", party);
        Assert.Contains("lyra", party);

        var late = ProgressAt(901);
        late.Flags.Add(StoryDatabase.FlagMarkSpared);
        Assert.Contains("mark", Manager(late).GetParty().Select(c => c.Id));
        Assert.DoesNotContain("soren", Manager(late).GetParty().Select(c => c.Id));
    }

    [Fact]
    public void Choices_RequireWonStage_AndGateRedemption()
    {
        var early = Manager(ProgressAt(100));
        Assert.Equal(ErrorCode.StageLocked, early.MakeChoice("lyra_truth", "forgive").Error);

        var story = Manager(ProgressAt(1001));
        Assert.Equal(ErrorCode.FeatureLocked, story.MakeChoice(StoryDatabase.FinalChoiceId, "redemption").Error);
        Assert.Equal(new[] { StoryEnding.Sacrifice, StoryEnding.Corruption }, story.GetAvailableEndings());

        Assert.True(story.MakeChoice("lyra_truth", "forgive").Success);
        Assert.Equal(ErrorCode.AlreadyClaimed, story.MakeChoice("lyra_truth", "condemn").Error);
        Assert.True(story.MakeChoice("spare_mark", "spare").Success);
        Assert.Contains(StoryEnding.Redemption, story.GetAvailableEndings());

        Assert.True(story.MakeChoice(StoryDatabase.FinalChoiceId, "corruption").Success);
        Assert.Equal(StoryEnding.Corruption, story.Progress.Ending);
        Assert.True(story.Progress.NewGamePlusUnlocked);
    }

    [Fact]
    public void StoryEvents_AreOrdered_MiraDiesBeforeMarkChoice()
    {
        var events = StoryDatabase.GetEvents(Fixtures.Balance.Story).Where(e => e.StageId == 760).ToList();
        int death = events.FindIndex(e => e.CharacterLeaves == "mira");
        int choice = events.FindIndex(e => e.ChoiceId == "spare_mark");
        Assert.True(death >= 0 && choice > death);
        Assert.Equal(150, StoryDatabase.GetEvents(Fixtures.Balance.Story).Count(e => e.Id.StartsWith("ch")));
    }
}
