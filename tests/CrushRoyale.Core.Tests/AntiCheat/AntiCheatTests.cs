using CrushRoyale.Core.AntiCheat;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Tests.Economy;
using CrushRoyale.Core.Tests.Gameplay;

namespace CrushRoyale.Core.Tests.AntiCheat;

public class AntiCheatTests
{
    private readonly ManualClock _clock = EconomyFixtures.Clock();
    private readonly InMemoryFlagStore _flags = new();

    private AntiCheatManager Manager() => new(Fixtures.Balance, _clock, _flags);

    private static (GameSession session, SessionConfig config) Match(ulong seed = 77)
    {
        var config = SessionConfig.ForPvp(seed, Fixtures.Balance, GameMode.PvpRanked, null, League.Gold);
        var session = new GameSession(config, Fixtures.Balance, "p");
        HeadlessRunner.Run(session, new GreedyBot(700));
        return (session, config);
    }

    [Fact]
    public void ValidReplay_RaisesNothing()
    {
        var (session, config) = Match();
        var record = new PlayerIntegrityRecord { PlayerId = "p" };
        var ac = Manager();
        Assert.True(ac.ValidateReplay(record, session.Replay, config).Valid);
        Assert.True(ac.ValidateScore(session.Score, session.Replay, config));
        Assert.False(ac.ValidateScore(session.Score + 1, session.Replay, config));
        Assert.True(ac.ValidateScore(session.Score, new GameSession(config, Fixtures.Balance).Board, session.Replay, config));
        Assert.Empty(_flags.ForPlayer("p"));
    }

    [Fact]
    public void ForgedScore_IsSevere_PermanentBan()
    {
        var (session, config) = Match();
        var replay = session.Replay;
        replay.Actions[^1] = new ReplayAction(replay.Actions[^1].Action, replay.Actions[^1].ScoreAfter + 9999);
        replay.FinalScore += 9999;

        var record = new PlayerIntegrityRecord { PlayerId = "p" };
        var ac = Manager();
        Assert.False(ac.ValidateReplay(record, replay, config, "m1").Valid);

        var flag = Assert.Single(_flags.ForPlayer("p"));
        Assert.Equal(CheatSeverity.Severe, flag.Severity);
        Assert.True(record.PermanentlyBanned);
        Assert.True(ac.IsBanned(record));
    }

    [Fact]
    public void BotSpeed_IsConfirmed_TemporaryBanAndTrophyFreeze()
    {
        var (session, config) = Match();
        var replay = session.Replay;
        replay.Actions[3] = new ReplayAction(replay.Actions[3].Action.WithTimestamp(replay.Actions[2].Action.TimestampMs + 50), replay.Actions[3].ScoreAfter);

        var record = new PlayerIntegrityRecord { PlayerId = "p" };
        var ac = Manager();
        ac.ValidateReplay(record, replay, config);

        Assert.Equal(FlagReason.ImpossibleTiming, Assert.Single(_flags.ForPlayer("p")).Reason);
        Assert.Equal(1, record.OffenseCount);
        Assert.False(record.PermanentlyBanned);
        Assert.True(ac.IsBanned(record));
        Assert.True(ac.AreTrophiesFrozen(record));
        _clock.Advance(TimeSpan.FromHours(25));
        Assert.False(ac.IsBanned(record));
    }

    [Fact]
    public void OutdatedClient_IsNotPunished()
    {
        var (session, config) = Match();
        var other = GameBalance.CreateDefault();
        other.ComboMeter.CascadeFill = 500;
        var record = new PlayerIntegrityRecord { PlayerId = "p" };
        var v = new AntiCheatManager(other, _clock, _flags).ValidateReplay(record, session.Replay, config);
        Assert.Equal(ErrorCode.VersionMismatch, v.Error);
        Assert.Empty(_flags.ForPlayer("p"));
        Assert.Equal(0, record.OffenseCount);
    }

    [Fact]
    public void WallClock_TimeTravel_IsDetected()
    {
        var (session, _) = Match();
        var ac = Manager();
        long now = TimeUtil.ToUnixMs(_clock.UtcNow);
        Assert.True(ac.ValidateTimestamps(new PlayerIntegrityRecord { PlayerId = "p" }, session.Replay, now - 120000, now));

        var cheater = new PlayerIntegrityRecord { PlayerId = "c" };
        Assert.False(ac.ValidateTimestamps(cheater, session.Replay, now - 30000, now));
        Assert.Equal(FlagReason.TimestampTampering, Assert.Single(_flags.ForPlayer("c")).Reason);
    }

    [Fact]
    public void ValidateMove_AndTiming()
    {
        var ac = Manager();
        var session = Fixtures.PvpSession();
        var hint = session.BoardManager.GetHint()!.Value;
        Assert.True(ac.ValidateMove(session.Board, hint.From, hint.To));
        Assert.False(ac.ValidateMove(session.Board, new Pos(0, 0), new Pos(5, 5)));
        Assert.True(ac.ValidateMoveTiming(1000, 650, 1650));
        Assert.False(ac.ValidateMoveTiming(1000, 650, 1300));
        Assert.False(ac.ValidateMoveTiming(1000, 0, 1100));
    }

    [Fact]
    public void Patterns_WinRateBoostingSpikeOutlier()
    {
        var ac = Manager();
        var record = new PlayerIntegrityRecord { PlayerId = "smurf" };
        long now = TimeUtil.ToUnixMs(_clock.UtcNow);

        for (int i = 0; i < 50; i++)
        {
            ac.AnalyzeMatch(record, new MatchSummary { MatchId = "m" + i, OpponentId = "o" + i, Won = true, TrophyDelta = 3, Score = 10000, AtUnixMs = now });
        }
        Assert.Contains(_flags.ForPlayer("smurf"), f => f.Reason == FlagReason.WinRate);
        Assert.Single(_flags.ForPlayer("smurf"), f => f.Reason == FlagReason.WinRate);

        var outlier = ac.AnalyzeMatch(record, new MatchSummary { MatchId = "big", OpponentId = "z", Won = true, Score = 90000, AtUnixMs = now });
        Assert.Contains(outlier, f => f.Reason == FlagReason.ScoreOutlier);

        var booster = new PlayerIntegrityRecord { PlayerId = "booster" };
        for (int i = 0; i < 10; i++)
        {
            ac.AnalyzeMatch(booster, new MatchSummary { MatchId = "b" + i, OpponentId = "friend", Won = true, TrophyDelta = 1, Score = 5000, AtUnixMs = now });
        }
        Assert.Contains(_flags.ForPlayer("booster"), f => f.Reason == FlagReason.SameOpponentWinRate);
        Assert.True(ac.IsInBoostingCooldown(booster));

        var climber = new PlayerIntegrityRecord { PlayerId = "climber" };
        for (int day = 0; day < 3; day++)
        {
            ac.AnalyzeMatch(climber, new MatchSummary { MatchId = "c" + day, OpponentId = "x" + day, Won = true, TrophyDelta = 250, Score = 1, AtUnixMs = now + day * 86400000L });
        }
        Assert.Contains(_flags.ForPlayer("climber"), f => f.Reason == FlagReason.TrophySpike);
    }

    [Fact]
    public void PunishmentLadder()
    {
        var ac = Manager();
        var first = ac.DecidePunishment(1, false);
        Assert.Equal(PunishmentAction.TemporaryBan, first.Action);
        Assert.True(first.FreezeTrophies);

        var second = ac.DecidePunishment(2, false);
        Assert.Equal(500, second.TrophyResetPermille);
        Assert.Equal(1000, AntiCheatManager.ApplyTrophyReset(2000, second));
        Assert.Equal(TimeUtil.ToUnixMs(_clock.UtcNow) + 168 * 3600000L, second.SuspendedUntilUnixMs);

        Assert.Equal(PunishmentAction.PermanentBan, ac.DecidePunishment(3, false).Action);
        Assert.Equal(PunishmentAction.PermanentBan, ac.DecidePunishment(1, true).Action);

        var record = new PlayerIntegrityRecord { PlayerId = "x" };
        Assert.True(ac.EnforcePunishment(record, 3));
        Assert.True(record.PermanentlyBanned);
    }

    [Fact]
    public void Device_ManyAccountsFlagged()
    {
        var ac = Manager();
        var registry = new DeviceRegistry();
        for (int i = 0; i < 3; i++)
        {
            ac.RegisterDeviceLogin(registry, "dev1", "acc" + i);
        }
        Assert.Empty(_flags.Pending());
        Assert.Equal(4, ac.RegisterDeviceLogin(registry, "dev1", "acc3"));
        Assert.Equal(4, _flags.Pending().Count(f => f.Reason == FlagReason.MultiAccountDevice));
    }
}
