using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.PowerUps;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Scoring;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Core.Tests.Gameplay;

internal static class Fixtures
{
    public static readonly GameBalance Balance = GameBalance.CreateDefault();

    public static StageData Stage(int moves = 20, int target = 1500, int timeMs = 60000, ulong seed = 1234, int bossHp = 0, int bossPhases = 0)
    {
        var stage = new StageData
        {
            Id = 7,
            TimeLimitMs = timeMs,
            MoveLimit = moves,
            TargetScore = target,
            TwoStarScore = target * 3 / 2,
            ThreeStarScore = target * 2,
            DifficultyPermille = 300,
            Seed = seed,
            BossHp = bossHp,
            BossPhases = bossPhases,
            BossStonesPerPhase = bossPhases > 0 ? 3 : 0
        };
        stage.Objectives.Add(bossHp > 0
            ? new StageObjective { Type = ObjectiveType.DefeatBoss, Target = bossHp }
            : new StageObjective { Type = ObjectiveType.ReachScore, Target = target });
        return stage;
    }

    public static GameSession StorySession(StageData? stage = null, League league = League.Master, params LoadoutEntry[] loadout) =>
        new(SessionConfig.ForStage(stage ?? Stage(), Balance, loadout, league), Balance, "player-1");

    public static GameSession PvpSession(ulong seed = 99, League league = League.Master, params LoadoutEntry[] loadout) =>
        new(SessionConfig.ForPvp(seed, Balance, GameMode.PvpRanked, loadout, league), Balance, "player-1");

    /// <summary>Plays the hint move at the earliest allowed time.</summary>
    public static ActionOutcome PlayHint(GameSession session, int extraDelay = 0)
    {
        var hint = session.BoardManager.GetHint()!.Value;
        return session.AttemptMove(hint.From, hint.To, session.NextActionAllowedAtMs + extraDelay);
    }
}

public class CascadeCalculatorTests
{
    private readonly CascadeCalculator _calc = new(Fixtures.Balance);

    [Theory]
    [InlineData(0, 1.0f)]
    [InlineData(1, 1.0f)]
    [InlineData(2, 1.5f)]
    [InlineData(3, 2.0f)]
    [InlineData(4, 3.0f)]
    [InlineData(9, 3.0f)]
    public void CascadeMultipliers_FollowGddTable(int level, float expected)
    {
        Assert.Equal(expected, _calc.GetCascadeMultiplier(level));
        Assert.Equal(100f * expected, _calc.CalculateCascadeScore(100f, level));
    }

    [Fact]
    public void ChainBonus_AddsTenPercentPerCascade_Capped()
    {
        Assert.Equal(0, _calc.GetChainBonusPermille(0));
        Assert.Equal(400, _calc.GetChainBonusPermille(4));
        Assert.Equal(1000, _calc.GetChainBonusPermille(25));
    }

    [Fact]
    public void ScoreSequence_AppliesLevelMultiplierThenChainBonus()
    {
        _calc.BeginSequence(0);
        var s0 = new ResolutionStep { CascadeLevel = 0, BasePoints = 100 };
        var s1 = new ResolutionStep { CascadeLevel = 1, BasePoints = 100 };
        var s2 = new ResolutionStep { CascadeLevel = 2, BasePoints = 100 };
        Assert.Equal(100, _calc.ScoreStep(s0));
        Assert.Equal(100, _calc.ScoreStep(s1));
        Assert.Equal(150, _calc.ScoreStep(s2));
        Assert.Equal(2, _calc.CalculateCascadeLevel());
        // (100 + 100 + 150) * 1.2
        Assert.Equal(420, _calc.FinishSequence(2));
        Assert.Equal(2, _calc.TotalCascades);
    }

    [Fact]
    public void CascadeInfinity_DoublesOnlyCascadeLevels()
    {
        _calc.BeginSequence(0);
        Assert.Equal(100, _calc.ScoreStep(new ResolutionStep { CascadeLevel = 0, BasePoints = 100 }, 2000));
        Assert.Equal(300, _calc.ScoreStep(new ResolutionStep { CascadeLevel = 2, BasePoints = 100 }, 2000));
    }

    [Fact]
    public void ComboMeter_ActivatesRedSurge_ForTwentySeconds()
    {
        Assert.False(_calc.AddComboFill(600, 1000));
        Assert.True(_calc.AddComboFill(500, 2000));
        Assert.Equal(0, _calc.ComboMeterPermille);
        Assert.False(_calc.IsRedSurgeActive(2000));
        Assert.True(_calc.IsRedSurgeActive(2001));
        Assert.Equal(2000, _calc.GetComboMultiplierPermille(15000));
        Assert.True(_calc.IsRedSurgeActive(22000));
        Assert.False(_calc.IsRedSurgeActive(22001));

        // No filling while the surge runs.
        Assert.False(_calc.AddComboFill(1000, 5000));
        Assert.Equal(0, _calc.ComboMeterPermille);
    }

    [Fact]
    public void UpdateComboMeter_UsesPercentPoints()
    {
        Assert.False(_calc.UpdateComboMeter(35f));
        Assert.Equal(350, _calc.ComboMeterPermille);
        Assert.True(_calc.UpdateComboMeter(65f));
        Assert.Equal(1.0f, _calc.GetComboMultiplier());
    }

    [Fact]
    public void ComboFill_ByShapeAndCascade()
    {
        var step = new ResolutionStep { CascadeLevel = 1 };
        Assert.Equal(150, _calc.GetComboFill(step));
    }

    [Fact]
    public void MegaCascade_TriggersAnimation()
    {
        var kinds = new List<CascadeAnimationKind>();
        _calc.AnimationTriggered += t => kinds.Add(t.Kind);
        _calc.BeginSequence(0);
        _calc.ScoreStep(new ResolutionStep { CascadeLevel = 1, BasePoints = 10 });
        _calc.ScoreStep(new ResolutionStep { CascadeLevel = 4, BasePoints = 10 });
        Assert.Equal(new[] { CascadeAnimationKind.Cascade, CascadeAnimationKind.MegaCascade }, kinds);
    }
}

public class PowerUpTests
{
    [Theory]
    [InlineData(PowerUpType.ChronoBomb, 0, 50, 5)]
    [InlineData(PowerUpType.ChronoBomb, 10, 35, 4)]
    [InlineData(PowerUpType.NuclearBomb, 5, 425, 43)]
    [InlineData(PowerUpType.CascadeInfinity, 20, 385, 39)]
    public void Prices_ApplyCappedVipDiscount(PowerUpType type, int vip, int coins, int orbes)
    {
        var price = PowerUpManager.GetPowerUpPrice(Fixtures.Balance, type, vip);
        Assert.Equal(coins, price.Coins);
        Assert.Equal(orbes, price.Orbes);
    }

    [Theory]
    [InlineData(PowerUpType.BrightSpark, League.Bronze, true)]
    [InlineData(PowerUpType.GoldenChain, League.Bronze, false)]
    [InlineData(PowerUpType.GoldenChain, League.Silver, true)]
    [InlineData(PowerUpType.FireStorm, League.Silver, false)]
    [InlineData(PowerUpType.FireStorm, League.Gold, true)]
    public void Unlocks_FollowLeagues(PowerUpType type, League league, bool unlocked)
    {
        Assert.Equal(unlocked, PowerUpManager.IsUnlocked(Fixtures.Balance, type, league));
        Assert.Equal(unlocked, PowerUpManager.IsPowerUpAvailable(Fixtures.Balance, (int)type, league));
    }

    [Fact]
    public void IsPowerUpAvailable_RejectsUnknownIds()
    {
        Assert.False(PowerUpManager.IsPowerUpAvailable(Fixtures.Balance, 42, League.Master));
    }

    [Fact]
    public void LoadoutValidation()
    {
        var b = Fixtures.Balance;
        var stage = Fixtures.Stage();
        Assert.Equal(ErrorCode.None, SessionConfig.ForStage(stage, b, new[] { new LoadoutEntry(PowerUpType.ChronoBomb, 3) }, League.Bronze).Validate(b));
        Assert.Equal(ErrorCode.PowerUpLocked, SessionConfig.ForStage(stage, b, new[] { new LoadoutEntry(PowerUpType.NuclearBomb, 1) }, League.Silver).Validate(b));
        Assert.Equal(ErrorCode.PowerUpNotAvailableInMode, SessionConfig.ForStage(stage, b, new[] { new LoadoutEntry(PowerUpType.FreezingGel, 1) }, League.Master).Validate(b));
        Assert.Equal(ErrorCode.LimitReached, SessionConfig.ForStage(stage, b, new[] { new LoadoutEntry(PowerUpType.Multiplier2x, 2) }, League.Master).Validate(b));
        Assert.Equal(ErrorCode.LimitReached, SessionConfig.ForStage(stage, b, new[]
        {
            new LoadoutEntry(PowerUpType.ChronoBomb, 1), new LoadoutEntry(PowerUpType.CoinBooster, 1),
            new LoadoutEntry(PowerUpType.BrightSpark, 1), new LoadoutEntry(PowerUpType.GoldenChain, 1)
        }, League.Master).Validate(b));
        Assert.Equal(ErrorCode.None, SessionConfig.ForPvp(1, b, GameMode.PvpRanked, new[] { new LoadoutEntry(PowerUpType.FreezingGel, 1) }, League.Silver).Validate(b));
    }

    [Fact]
    public void ChronoBomb_ExtendsTimer_AndIsReusable()
    {
        var session = Fixtures.StorySession(null, League.Bronze, new LoadoutEntry(PowerUpType.ChronoBomb, 2));
        int before = session.TimeLimitMs;
        Assert.True(session.ActivatePowerUp(PowerUpType.ChronoBomb, null, 100).Accepted);
        Assert.True(session.ActivatePowerUp(PowerUpType.ChronoBomb, null, session.NextActionAllowedAtMs).Accepted);
        Assert.Equal(before + 40000, session.TimeLimitMs);
        Assert.Equal(ErrorCode.NotEnoughItems, session.ActivatePowerUp(PowerUpType.ChronoBomb, null, session.NextActionAllowedAtMs).Error);
    }

    [Fact]
    public void OncePerMatch_IsEnforced()
    {
        var session = Fixtures.PvpSession(5, League.Master, new LoadoutEntry(PowerUpType.Multiplier2x, 1));
        Assert.True(session.ActivatePowerUp(PowerUpType.Multiplier2x, null, 0).Accepted);
        Assert.Equal(ErrorCode.PowerUpAlreadyUsed, session.ActivatePowerUp(PowerUpType.Multiplier2x, null, session.NextActionAllowedAtMs).Error);
        Assert.Equal(2000, session.PowerUps.GetScoreMultiplierPermille(1000));
        Assert.Equal(1000, session.PowerUps.GetScoreMultiplierPermille(15000));
    }

    [Fact]
    public void NuclearBomb_RequiresTarget_AndClearsArea()
    {
        var session = Fixtures.StorySession(null, League.Master, new LoadoutEntry(PowerUpType.NuclearBomb, 1));
        Assert.Equal(ErrorCode.PowerUpNeedsTarget, session.ActivatePowerUp(PowerUpType.NuclearBomb, null, 0).Error);
        var outcome = session.ActivatePowerUp(PowerUpType.NuclearBomb, new Pos(3, 3), 0);
        Assert.True(outcome.Accepted);
        Assert.True(outcome.Resolution!.Steps[0].Cleared.Count >= 25);
        Assert.True(outcome.PointsGained > 0);
        Assert.Equal(20, session.MovesLeft);
    }

    [Fact]
    public void FireStorm_ClearsRedAndOrange()
    {
        var session = Fixtures.StorySession(null, League.Master, new LoadoutEntry(PowerUpType.FireStorm, 1));
        int redOrange = session.Board.CountColor(PieceColor.Red) + session.Board.CountColor(PieceColor.Orange);
        var outcome = session.ActivatePowerUp(PowerUpType.FireStorm, null, 0);
        var first = outcome.Resolution!.Steps[0];
        Assert.Equal(redOrange, first.ClearedByColor[(int)PieceColor.Red] + first.ClearedByColor[(int)PieceColor.Orange]);
    }

    [Fact]
    public void FreezingGel_RecordsWindow_InPvp()
    {
        var session = Fixtures.PvpSession(5, League.Master, new LoadoutEntry(PowerUpType.FreezingGel, 1));
        Assert.True(session.ActivatePowerUp(PowerUpType.FreezingGel, null, 3000).Accepted);
        var window = Assert.Single(session.PowerUps.GetFreezeWindows());
        Assert.Equal(3000, window.StartMs);
        Assert.Equal(13000, window.EndMs);
    }

    [Fact]
    public void ActivateById_UsesEarliestAllowedTime()
    {
        var session = Fixtures.StorySession(null, League.Bronze, new LoadoutEntry(PowerUpType.CoinBooster, 1));
        Assert.True(session.ActivatePowerUp((int)PowerUpType.CoinBooster).Accepted);
        Assert.Equal(ErrorCode.InvalidArgument, session.ActivatePowerUp(99).Error);
    }
}

public class GameSessionTests
{
    [Fact]
    public void ValidMove_ConsumesMove_AndScores()
    {
        var session = Fixtures.StorySession();
        var outcome = Fixtures.PlayHint(session, 500);
        Assert.True(outcome.Accepted);
        Assert.Equal(19, session.MovesLeft);
        Assert.True(session.Score > 0);
        Assert.Equal(session.Score, outcome.ScoreAfter);
        Assert.True(outcome.AnimationDurationMs >= Fixtures.Balance.Timing.SwapAnimationMs + Fixtures.Balance.Timing.CascadeStepMs);
    }

    [Fact]
    public void InvalidSwap_DoesNotConsumeMove()
    {
        var session = Fixtures.StorySession();
        var outcome = session.AttemptMove(new Pos(0, 0), new Pos(3, 3), 100);
        Assert.False(outcome.Accepted);
        Assert.Equal(ErrorCode.NotAdjacent, outcome.Error);
        Assert.Equal(20, session.MovesLeft);
    }

    [Fact]
    public void Timing_RulesAreEnforced()
    {
        var session = Fixtures.StorySession();
        var first = Fixtures.PlayHint(session, 1000);
        Assert.True(first.Accepted);

        var hint = session.BoardManager.GetHint()!.Value;
        Assert.Equal(ErrorCode.TooFast, session.AttemptMove(hint.From, hint.To, first.Action.TimestampMs + 50).Error);
        Assert.Equal(ErrorCode.TimestampOutOfOrder, session.AttemptMove(hint.From, hint.To, 10).Error);
        Assert.Equal(ErrorCode.TimeExpired, session.AttemptMove(hint.From, hint.To, 60000).Error);
    }

    [Fact]
    public void ReachingTarget_WinsEarly_WithBonus()
    {
        var session = Fixtures.StorySession(Fixtures.Stage(moves: 20, target: 50));
        bool? won = null;
        session.OnGameEnd += (w, _) => won = w;

        Fixtures.PlayHint(session, 500);

        Assert.Equal(SessionState.Won, session.State);
        Assert.True(won);
        var result = session.GetResult();
        Assert.True(result.BonusPoints >= 19 * Fixtures.Balance.Scoring.RemainingMoveBonus);
        Assert.Equal(3, result.Stars);
    }

    [Fact]
    public void RunningOutOfMoves_Loses()
    {
        var session = Fixtures.StorySession(Fixtures.Stage(moves: 3, target: 1_000_000));
        for (int i = 0; i < 3; i++)
        {
            Assert.True(Fixtures.PlayHint(session, 300).Accepted);
        }
        Assert.Equal(SessionState.Lost, session.State);
        Assert.Equal(ErrorCode.SessionOver, Fixtures.PlayHint(session, 300).Error);
    }

    [Fact]
    public void Tick_FiresWarningOnce_ThenEndsOnTime()
    {
        var session = Fixtures.StorySession(Fixtures.Stage(timeMs: 30000));
        int warnings = 0;
        session.OnTimeWarning += _ => warnings++;
        session.Tick(15000);
        session.Tick(21000);
        session.Tick(25000);
        Assert.Equal(1, warnings);
        Assert.True(session.IsRunning);
        session.Tick(30000);
        Assert.Equal(SessionState.Lost, session.State);
        Assert.Equal(30000, session.EndTimeMs);
    }

    [Fact]
    public void Continue_ReopensLostStage_WithExtraMoves()
    {
        var session = Fixtures.StorySession(Fixtures.Stage(moves: 1, target: 1_000_000));
        Fixtures.PlayHint(session, 300);
        Assert.Equal(SessionState.Lost, session.State);

        var outcome = session.Continue(session.EndTimeMs + 5000);
        Assert.True(outcome.Accepted);
        Assert.True(session.IsRunning);
        Assert.Equal(Fixtures.Balance.Stamina.ContinueExtraMoves, session.MovesLeft);
        Assert.Equal(1, session.ContinuesUsed);
    }

    [Fact]
    public void Continue_IsRejectedInPvp()
    {
        var session = Fixtures.PvpSession();
        session.FinishByTime();
        Assert.Equal(ErrorCode.PowerUpNotAvailableInMode, session.Continue(95000).Error);
    }

    [Fact]
    public void PvpSession_EndsOnlyByTime()
    {
        var session = Fixtures.PvpSession();
        Assert.False(session.Config.HasMoveLimit);
        var result = HeadlessRunner.Run(session, new GreedyBot());
        Assert.Equal(SessionState.Completed, result.State);
        Assert.True(result.MovesUsed > 30);
        Assert.Equal(Fixtures.Balance.Pvp.TimeLimitMs, result.DurationMs);
    }

    [Fact]
    public void BossPhases_AddStones()
    {
        var session = Fixtures.StorySession(Fixtures.Stage(moves: 40, target: 0, bossHp: 900, bossPhases: 3));
        var phases = new List<int>();
        session.OnBossPhaseChanged += (phase, _) => phases.Add(phase);
        HeadlessRunner.Run(session, new GreedyBot());
        Assert.Contains(1, phases);
        Assert.True(session.GetResult().Won || session.Score < 900);
    }

    [Fact]
    public void GreedyBot_BeatsRandomBot_OnAverage()
    {
        long greedy = 0, random = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            greedy += HeadlessRunner.Run(Fixtures.PvpSession(seed), new GreedyBot()).FinalScore;
            random += HeadlessRunner.Run(Fixtures.PvpSession(seed), new RandomBot(seed)).FinalScore;
        }
        Assert.True(greedy > random, $"greedy {greedy} vs random {random}");
    }

    [Fact]
    public void SkilledBots_GetStrongerWithTheLeague()
    {
        long bronze = 0, master = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            bronze += HeadlessRunner.Run(Fixtures.PvpSession(seed), SkilledBot.ForLeague(League.Bronze, seed)).FinalScore;
            master += HeadlessRunner.Run(Fixtures.PvpSession(seed), SkilledBot.ForLeague(League.Master, seed)).FinalScore;
        }
        Assert.True(master > bronze * 3 / 2, $"master {master} vs bronze {bronze}");
    }

    [Fact]
    public async Task GameplayCore_RunsAsyncLoop()
    {
        var core = new GameplayCore(Fixtures.Balance);
        var time = new FakeTime();
        var presenter = new FakePresenter(time);
        var result = await core.PlayStage(Fixtures.Stage(moves: 5, target: 1_000_000), new GreedyBot(200), presenter, time);
        Assert.Equal(SessionState.Lost, result.State);
        Assert.Equal(5, result.MovesUsed);
        Assert.Equal(5, presenter.Presented);
    }

    private sealed class FakeTime : IGameTimeSource
    {
        public int NowMs { get; set; }
    }

    private sealed class FakePresenter(FakeTime time) : IActionPresenter
    {
        public int Presented { get; private set; }

        public Task PresentAsync(ActionOutcome outcome, CancellationToken cancellationToken)
        {
            Presented++;
            time.NowMs += outcome.AnimationDurationMs;
            return Task.CompletedTask;
        }

        public Task NextFrameAsync(CancellationToken cancellationToken)
        {
            time.NowMs += 16;
            return Task.CompletedTask;
        }
    }
}

public class ReplayTests
{
    private static (GameSession session, SessionConfig config) PlayPvp(ulong seed, params LoadoutEntry[] loadout)
    {
        var config = SessionConfig.ForPvp(seed, Fixtures.Balance, GameMode.PvpRanked, loadout, League.Master);
        var session = new GameSession(config, Fixtures.Balance, "ghost-owner");
        int t = 800;
        if (loadout.Any(l => l.Type == PowerUpType.CascadeInfinity))
        {
            Assert.True(session.ActivatePowerUp(PowerUpType.CascadeInfinity, null, 400).Accepted);
            t = session.NextActionAllowedAtMs + 300;
        }
        HeadlessRunner.Run(session, new GreedyBot(700));
        return (session, config);
    }

    [Fact]
    public void Replay_RoundTripsThroughBinary()
    {
        var (session, _) = PlayPvp(31, new LoadoutEntry(PowerUpType.CascadeInfinity, 1));
        byte[] bytes = ReplaySerializer.Serialize(session.Replay);
        var copy = ReplaySerializer.Deserialize(bytes);

        Assert.True(bytes.Length < 4096, $"Replay is {bytes.Length} bytes");
        Assert.Equal(session.Replay.Seed, copy.Seed);
        Assert.Equal(session.Replay.Actions.Count, copy.Actions.Count);
        Assert.Equal(session.Replay.FinalScore, copy.FinalScore);
        Assert.Equal(session.Replay.Checkpoints.Select(c => c.BoardHash), copy.Checkpoints.Select(c => c.BoardHash));
        Assert.Equal(session.Replay.GetMoves().Select(m => m.ResultingScore), copy.GetMoves().Select(m => m.ResultingScore));
    }

    [Fact]
    public void Replay_ResimulatesToSameResult()
    {
        var (session, config) = PlayPvp(32, new LoadoutEntry(PowerUpType.CascadeInfinity, 1));
        var copy = ReplaySerializer.Deserialize(ReplaySerializer.Serialize(session.Replay));
        var verification = ReplaySimulator.Verify(copy, config, Fixtures.Balance);
        Assert.True(verification.Valid, verification.Reason);
        Assert.Equal(session.Score, verification.AuthoritativeScore);
    }

    [Fact]
    public void StoryReplay_WithContinue_Resimulates()
    {
        var stage = Fixtures.Stage(moves: 2, target: 1_000_000);
        var session = Fixtures.StorySession(stage);
        Fixtures.PlayHint(session, 400);
        Fixtures.PlayHint(session, 400);
        session.Continue(session.EndTimeMs + 2500);
        Fixtures.PlayHint(session, 400);
        session.Abandon(session.NextActionAllowedAtMs + 100);

        var config = SessionConfig.ForStage(stage, Fixtures.Balance, null, League.Master);
        var verification = ReplaySimulator.Verify(session.Replay, config, Fixtures.Balance);
        Assert.True(verification.Valid, verification.Reason);
    }

    [Fact]
    public void WonStoryReplay_WithEndBonus_Resimulates()
    {
        var stage = Fixtures.Stage(moves: 25, target: 600);
        var session = Fixtures.StorySession(stage, League.Bronze, new LoadoutEntry(PowerUpType.ChronoBomb, 2));
        HeadlessRunner.Run(session, new GreedyBot());
        Assert.Equal(SessionState.Won, session.State);
        Assert.True(session.GetResult().BonusPoints > 0);

        var config = SessionConfig.ForStage(stage, Fixtures.Balance, new[] { new LoadoutEntry(PowerUpType.ChronoBomb, 2) }, League.Bronze);
        var copy = ReplaySerializer.Deserialize(ReplaySerializer.Serialize(session.Replay));
        var verification = ReplaySimulator.Verify(copy, config, Fixtures.Balance);
        Assert.True(verification.Valid, verification.Reason);
        Assert.Equal(session.Score, verification.AuthoritativeScore);
    }

    [Fact]
    public void TamperedScore_IsDetected()
    {
        var (session, config) = PlayPvp(33);
        var replay = session.Replay;
        var last = replay.Actions[^1];
        replay.Actions[^1] = new ReplayAction(last.Action, last.ScoreAfter + 5000);
        replay.FinalScore += 5000;

        var verification = ReplaySimulator.Verify(replay, config, Fixtures.Balance);
        Assert.False(verification.Valid);
        Assert.Equal(ErrorCode.ReplayMismatch, verification.Error);
        Assert.Equal(replay.Actions.Count - 1, verification.FailedActionIndex);
    }

    [Fact]
    public void BotSpeedMoves_AreRejected()
    {
        var (session, config) = PlayPvp(34);
        var replay = session.Replay;
        var second = replay.Actions[1];
        replay.Actions[1] = new ReplayAction(second.Action.WithTimestamp(replay.Actions[0].Action.TimestampMs + 10), second.ScoreAfter);

        var verification = ReplaySimulator.Verify(replay, config, Fixtures.Balance);
        Assert.False(verification.Valid);
        Assert.Contains("TooFast", verification.Reason);
    }

    [Fact]
    public void DifferentBalance_IsVersionMismatch_NotCheating()
    {
        var (session, config) = PlayPvp(35);
        var otherBalance = GameBalance.CreateDefault();
        otherBalance.Scoring.PointsPerPiece = 25;
        Assert.Equal(ErrorCode.VersionMismatch, ReplaySimulator.Verify(session.Replay, config, otherBalance).Error);
    }

    [Fact]
    public void CorruptedBytes_AreRejected()
    {
        var (session, _) = PlayPvp(36);
        byte[] bytes = ReplaySerializer.Serialize(session.Replay);

        var flipped = (byte[])bytes.Clone();
        flipped[20] ^= 0x55;
        Assert.False(ReplaySerializer.TryDeserialize(flipped).Success);
        Assert.False(ReplaySerializer.TryDeserialize(bytes.Take(bytes.Length - 9).ToArray()).Success);
        Assert.Throws<ReplayFormatException>(() => ReplaySerializer.Deserialize(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
    }
}
