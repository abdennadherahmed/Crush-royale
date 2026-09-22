using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Tests.Gameplay;

namespace CrushRoyale.Core.Tests.Pvp;

public class TrophyCalculatorTests
{
    private static readonly TrophyBalance B = GameBalance.CreateDefault().Trophies;

    [Theory]
    [InlineData(1000, 1000, 25)]
    [InlineData(1000, 1150, 47)]
    [InlineData(1000, 1300, 68)]
    [InlineData(1000, 800, 10)]
    [InlineData(0, 3000, 80)]
    public void Win(int player, int opponent, int expected)
    {
        Assert.Equal(expected, TrophyCalculator.Calculate(B, player, opponent, MatchOutcome.Win).Delta);
    }

    [Theory]
    [InlineData(1000, 1300, -5)]
    [InlineData(1000, 1050, -15)]
    [InlineData(1000, 800, -35)]
    [InlineData(60, 0, -35)]
    [InlineData(10, 10, -10)]
    public void Loss_NeverBelowZero(int player, int opponent, int expected)
    {
        var change = TrophyCalculator.Calculate(B, player, opponent, MatchOutcome.Loss);
        Assert.Equal(expected, change.Delta);
        Assert.True(change.After >= 0);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(2, 35)]
    [InlineData(4, 45)]
    [InlineData(10, 45)]
    public void StreakBonus_IsCapped(int streak, int expected)
    {
        Assert.Equal(expected, TrophyCalculator.Calculate(B, 1000, 1000, MatchOutcome.Win, streak).Delta);
    }

    [Theory]
    [InlineData(1, 12)]
    [InlineData(2, 6)]
    [InlineData(10, 1)]
    public void RepeatedOpponent_HalvesGains(int recent, int expected)
    {
        Assert.Equal(expected, TrophyCalculator.Calculate(B, 1000, 1000, MatchOutcome.Win, 0, recent).Delta);
    }

    [Fact]
    public void Draw_ChangesNothing()
    {
        Assert.Equal(0, TrophyCalculator.Calculate(B, 1000, 2000, MatchOutcome.Draw).Delta);
        Assert.Equal(25, TrophyCalculator.CalculateTrophyGain(B, 1000, 1000, true));
        Assert.Equal(-15, TrophyCalculator.CalculateTrophyGain(B, 1000, 1000, false));
    }

    [Theory]
    [InlineData(0, League.Bronze)]
    [InlineData(299, League.Bronze)]
    [InlineData(300, League.Silver)]
    [InlineData(1199, League.Gold)]
    [InlineData(1800, League.Diamond)]
    [InlineData(2499, League.Diamond)]
    [InlineData(2500, League.Master)]
    public void Leagues(int trophies, League league)
    {
        Assert.Equal(league, LeagueTable.GetLeague(trophies, B));
        Assert.Equal(league.ToString(), LeagueTable.GetLeagueName(trophies, B));
    }
}

public class TrophySystemTests
{
    [Fact]
    public void RecordMatch_UpdatesStreaks_LeagueAndHistory()
    {
        var clock = new ManualClock(new DateTime(2026, 9, 14, 12, 0, 0));
        var system = new TrophySystem(Fixtures.Balance, clock);
        var record = new PlayerTrophyRecord { PlayerId = "p1", Trophies = 290 };

        var change = system.RecordMatch(record, "p2", 290, MatchOutcome.Win, "m1");
        Assert.True(change.Promoted);
        Assert.Equal(10, change.FirstReachOrbes);
        Assert.Equal(League.Silver, record.HighestLeague);
        Assert.Equal(1, system.GetCurrentWinStreak(record));

        // Second win vs the same player the same day: halved, plus streak bonus.
        var second = system.RecordMatch(record, "p2", 290, MatchOutcome.Win, "m2");
        Assert.Equal(1, second.RepeatGamesPenalized);

        system.RecordMatch(record, "p3", 400, MatchOutcome.Loss, "m3");
        Assert.Equal(0, record.CurrentWinStreak);
        Assert.Equal(2, record.BestWinStreak);
        Assert.Equal(3, record.History.Count);

        // Falling back to Bronze does not re-lock Silver unlocks.
        record.Trophies = 10;
        system.RecordMatch(record, "p4", 10, MatchOutcome.Loss, "m4");
        Assert.Equal(League.Silver, record.HighestLeague);
    }

    [Fact]
    public void SeasonReset_Soft_Rewards_AndArchive()
    {
        var balance = GameBalance.CreateDefault();
        var clock = new ManualClock(new DateTime(2026, 9, 13, 20, 0, 0));
        int week = TimeUtil.WeekIndex(clock.UtcNow);
        var system = new TrophySystem(balance, clock);

        var players = new List<PlayerTrophyRecord>
        {
            new() { PlayerId = "a", Trophies = 1000 },
            new() { PlayerId = "b", Trophies = 1100 },
            new() { PlayerId = "idle", Trophies = 2600 }
        };
        system.RecordMatch(players[0], "b", 1100, MatchOutcome.Win, "m1");
        system.RecordMatch(players[1], "a", 1000, MatchOutcome.Loss, "m1");

        var result = SeasonService.ResetSeasonalData(players, week, balance.Trophies);

        var b = result.Rewards.Single(r => r.PlayerId == "b");
        Assert.Equal(League.Gold, b.FinalLeague);
        Assert.Equal(1, b.RankInLeague);
        Assert.Equal(1000, b.Coins);
        Assert.Equal(6 + 100, b.Orbes);

        var idle = result.Rewards.Single(r => r.PlayerId == "idle");
        Assert.Equal(0, idle.Coins);
        Assert.Equal(300 + (2600 - 300) / 2, players[2].Trophies);
        Assert.Single(result.ArchivedTop[League.Master]);
    }

    [Theory]
    [InlineData(SeasonResetPolicy.Soft, 200, 200)]
    [InlineData(SeasonResetPolicy.Soft, 1000, 650)]
    [InlineData(SeasonResetPolicy.Full, 1000, 0)]
    public void ResetPolicies(SeasonResetPolicy policy, int before, int after)
    {
        var b = GameBalance.CreateDefault().Trophies;
        b.ResetPolicy = policy;
        Assert.Equal(after, SeasonService.ApplyReset(before, b));
    }

    [Fact]
    public void WeekIndex_ChangesAtSundayMidnightUtc()
    {
        var sundayLate = new DateTime(2026, 9, 13, 23, 59, 59, DateTimeKind.Utc);
        var mondayStart = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(DayOfWeek.Sunday, sundayLate.DayOfWeek);
        Assert.Equal(TimeUtil.WeekIndex(sundayLate) + 1, TimeUtil.WeekIndex(mondayStart));
        Assert.Equal(mondayStart, TimeUtil.NextWeeklyReset(sundayLate));
    }
}

public class GhostPlayTests
{
    private static GameSession Play(ulong seed, string playerId, IBotStrategy bot, params LoadoutEntry[] loadout)
    {
        var session = new GameSession(SessionConfig.ForPvp(seed, Fixtures.Balance, GameMode.PvpRanked, loadout, League.Master), Fixtures.Balance, playerId);
        HeadlessRunner.Run(session, bot);
        return session;
    }

    [Fact]
    public void ComputeResult_HigherScoreWins()
    {
        var a = Play(500, "alice", new GreedyBot(600));
        var b = Play(500, "bob", new RandomBot(1), new LoadoutEntry[0]);
        var result = PvpGhostPlay.ComputeResult(b.Replay, a.Replay, Fixtures.Balance);

        Assert.Equal("alice", result.WinnerId);
        Assert.Equal(MatchOutcome.Loss, result.ChallengerOutcome);
        Assert.Equal(a.Score, result.OpponentScore);
        Assert.Equal("alice", result.AsTuple().Item1);
    }

    [Fact]
    public void FreezingGel_VoidsOpponentPointsInWindow()
    {
        var victim = Play(501, "victim", new GreedyBot(600));

        var attacker = new GameSession(SessionConfig.ForPvp(501, Fixtures.Balance, GameMode.PvpRanked, new[] { new LoadoutEntry(PowerUpType.FreezingGel, 1) }, League.Master), Fixtures.Balance, "attacker");
        Assert.True(attacker.ActivatePowerUp(PowerUpType.FreezingGel, null, 2000).Accepted);
        HeadlessRunner.Run(attacker, new GreedyBot(600));

        long expected = victim.Replay.ScoreAt(11999) - victim.Replay.ScoreAt(1999);
        long frozen = PvpGhostPlay.FrozenPoints(victim.Replay, attacker.Replay, Fixtures.Balance);
        Assert.True(frozen > 0);
        Assert.Equal(expected, frozen);

        var result = PvpGhostPlay.ComputeResult(victim.Replay, attacker.Replay, Fixtures.Balance);
        Assert.Equal(victim.Score - frozen, result.ChallengerScore);
    }

    [Fact]
    public void GhostPlayer_FollowsAndRewinds()
    {
        var owner = Play(502, "owner", new GreedyBot(700));
        var ghost = new GhostPlayer(owner.Replay, PvpGhostPlay.GhostConfig(owner.Replay, Fixtures.Balance), Fixtures.Balance);
        int actions = 0;
        ghost.OnGhostAction += _ => actions++;

        ghost.AdvanceTo(30500);
        Assert.Equal(owner.Replay.ScoreAt(30000), ghost.CurrentScore);
        Assert.True(actions > 0);

        ghost.SeekTo(10000);
        Assert.Equal(owner.Replay.ScoreAt(10000), ghost.CurrentScore);

        ghost.SeekTo(95000);
        Assert.Equal(owner.Score, ghost.CurrentScore);
        Assert.True(ghost.IsFinished);
        Assert.False(ghost.Desynced);
    }

    [Fact]
    public void PvpGhostPlay_FullFlow()
    {
        var owner = Play(503, "owner", new GreedyBot(800));
        byte[] ghostBytes = ReplaySerializer.Serialize(owner.Replay);

        var play = new PvpGhostPlay(Fixtures.Balance);
        var live = play.StartMatch(SessionConfig.ForPvp(503, Fixtures.Balance, GameMode.PvpRanked, null, League.Gold), "challenger");
        Assert.True(play.LoadOpponentGhost(ghostBytes).Success);

        var bot = new GreedyBot(500);
        while (live.IsRunning)
        {
            int t = live.NextActionAllowedAtMs + 500;
            play.Update(t);
            if (!live.IsRunning)
            {
                break;
            }
            var action = bot.ChooseAction(live, t)!;
            if (!live.Apply(action).Accepted)
            {
                live.FinishByTime();
            }
        }
        play.Update(live.TimeLimitMs + 1000);

        Assert.Equal(owner.Score, play.Ghost.CurrentScore);
        var result = play.GetMatchResult();
        Assert.Equal(live.Score, result.ChallengerScore);
        Assert.True(ReplaySerializer.Deserialize(play.SerializeMoves()).IsFinished);
    }

    [Fact]
    public void LoadOpponentGhost_RejectsOtherBoard()
    {
        var owner = Play(504, "owner", new GreedyBot(900));
        var play = new PvpGhostPlay(Fixtures.Balance);
        play.StartMatch(SessionConfig.ForPvp(999, Fixtures.Balance, GameMode.PvpRanked, null, League.Bronze), "me");
        Assert.Equal(ErrorCode.ReplayMismatch, play.LoadOpponentGhost(ReplaySerializer.Serialize(owner.Replay)).Error);
        Assert.Equal(ErrorCode.ReplayInvalid, play.LoadOpponentGhost(new byte[] { 0, 1, 2 }).Error);
    }
}

public class MatchmakingTests
{
    private readonly ManualClock _clock = new(new DateTime(2026, 9, 14, 10, 0, 0));
    private readonly InMemoryGhostPool _ghosts = new();

    private MatchmakingEngine Engine() => new(Fixtures.Balance, _clock, _ghosts, 42);

    [Fact]
    public void CloseTrophies_MatchImmediately()
    {
        var mm = Engine();
        mm.RequestMatch("a", 1000, 0, "eu");
        mm.RequestMatch("b", 1050, 0, "eu");
        var found = Assert.Single(mm.ProcessQueue());

        var a = mm.GetTicket("a")!;
        var b = mm.GetTicket("b")!;
        Assert.Equal(TicketStatus.Matched, a.Status);
        Assert.Equal(a.MatchId, b.MatchId);
        Assert.Equal(a.Seed, b.Seed);
        Assert.Equal("b", a.OpponentId);
        Assert.False(found.VsGhost);
    }

    [Fact]
    public void RangeWidens_WhileWaiting()
    {
        var mm = Engine();
        mm.RequestMatch("a", 1000, 0, "eu");
        mm.RequestMatch("b", 1250, 0, "eu");
        Assert.Empty(mm.ProcessQueue());

        _clock.Advance(TimeSpan.FromSeconds(15));
        Assert.Single(mm.ProcessQueue());
    }

    [Fact]
    public void RangeNeverExceedsMax()
    {
        var mm = Engine();
        mm.RequestMatch("a", 1000, 0, "eu");
        mm.RequestMatch("b", 1400, 0, "eu");
        _clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Empty(mm.ProcessQueue());
    }

    [Fact]
    public void GhostFallback_AfterWait_ExcludesOwnGhosts()
    {
        var mm = Engine();
        long now = TimeUtil.ToUnixMs(_clock.UtcNow);
        _ghosts.Add(new GhostCandidate { ReplayId = "own", PlayerId = "a", Trophies = 1000, RecordedAtMs = now, Seed = 1 });
        _ghosts.Add(new GhostCandidate { ReplayId = "far", PlayerId = "x", Trophies = 2000, RecordedAtMs = now, Seed = 2 });
        _ghosts.Add(new GhostCandidate { ReplayId = "good", PlayerId = "y", Trophies = 1020, RecordedAtMs = now, Seed = 3 });

        mm.RequestMatch("a", 1000, 0, "eu");
        Assert.Empty(mm.ProcessQueue());

        _clock.Advance(TimeSpan.FromSeconds(9));
        var found = Assert.Single(mm.ProcessQueue());
        Assert.True(found.VsGhost);
        Assert.Equal("good", found.Ghost.ReplayId);
        Assert.Equal(3UL, mm.GetTicket("a")!.Seed);
        Assert.Equal("y", mm.FindOpponent("a"));
    }

    [Fact]
    public void Timeout_After30Seconds()
    {
        var mm = Engine();
        mm.RequestMatch("lonely", 1000, 0, "eu");
        _clock.Advance(TimeSpan.FromSeconds(31));
        mm.ProcessQueue();
        Assert.Equal(TicketStatus.TimedOut, mm.GetTicket("lonely")!.Status);
        Assert.Null(mm.FindOpponent("lonely"));
    }

    [Fact]
    public void Cancel_RemovesTicket()
    {
        var mm = Engine();
        mm.RequestMatch("a", 1000, 0, "eu");
        Assert.True(mm.CancelMatchRequest("a"));
        Assert.False(mm.CancelMatchRequest("a"));
        mm.RequestMatch("b", 1000, 0, "eu");
        Assert.Empty(mm.ProcessQueue());
    }

    [Fact]
    public void Fairness_NoQuickRematch_AndHourlyCap()
    {
        var mm = Engine();
        void Round()
        {
            mm.RequestMatch("a", 1000, 0, "eu");
            mm.RequestMatch("b", 1000, 0, "eu");
        }

        Round();
        Assert.Single(mm.ProcessQueue());
        mm.Acknowledge("a");
        mm.Acknowledge("b");

        Round();
        Assert.Empty(mm.ProcessQueue());
        mm.CancelMatchRequest("a");
        mm.CancelMatchRequest("b");

        for (int i = 0; i < 2; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(6));
            Round();
            Assert.Single(mm.ProcessQueue());
            mm.Acknowledge("a");
            mm.Acknowledge("b");
        }

        _clock.Advance(TimeSpan.FromMinutes(6));
        Round();
        Assert.Empty(mm.ProcessQueue());
    }

    [Fact]
    public void WinStreak_ShiftsTargetUpward()
    {
        var mm = Engine();
        mm.RequestMatch("streaker", 1000, 5, "eu");
        mm.RequestMatch("weaker", 920, 0, "eu");
        mm.RequestMatch("stronger", 1090, 0, "eu");
        mm.ProcessQueue();
        Assert.Equal("stronger", mm.GetTicket("streaker")!.OpponentId);
    }
}
