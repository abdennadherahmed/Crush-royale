using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Social;
using CrushRoyale.Core.Tests.Economy;
using CrushRoyale.Core.Tests.Gameplay;

namespace CrushRoyale.Core.Tests.Social;

public class GuildTests
{
    private readonly ManualClock _clock = EconomyFixtures.Clock();

    private GuildManager Manager() => new(Fixtures.Balance, _clock);

    private Guild NewGuild(GuildManager m, Wallet wallet)
    {
        var created = m.CreateGuild("g1", "Crystal Knights", "leader", "Leader", 1500, wallet, 41, false);
        Assert.True(created.Success, created.Message);
        return created.Value;
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("bad!name")]
    [InlineData("two  spaces")]
    [InlineData("The Fuckers")]
    [InlineData("A name that is far too long")]
    public void InvalidNames_AreRejected(string name)
    {
        Assert.Equal(ErrorCode.NameInvalid, Manager().ValidateName(name));
    }

    [Fact]
    public void UnicodeNames_AreAllowed()
    {
        Assert.Equal(ErrorCode.None, Manager().ValidateName("فرسان الكريستال"));
        Assert.Equal(ErrorCode.None, Manager().ValidateName("Scunthorpe United"));
    }

    [Fact]
    public void Create_RequiresUnlock_AndCostsCoins()
    {
        var m = Manager();
        var wallet = EconomyFixtures.Wallet(_clock, 1000, 0);
        Assert.Equal(ErrorCode.FeatureLocked, m.CreateGuild("g", "Valid Name", "p", "P", 0, wallet, 40, false).Error);
        Assert.Equal(ErrorCode.AlreadyMember, m.CreateGuild("g", "Valid Name", "p", "P", 0, wallet, 41, true).Error);
        var guild = NewGuild(m, wallet);
        Assert.Equal(900, wallet.Coins);
        Assert.Equal(GuildRole.Leader, guild.Find("leader")!.Role);
    }

    [Fact]
    public void Membership_Capacity_Roles_AndSuccession()
    {
        var m = Manager();
        var guild = NewGuild(m, EconomyFixtures.Wallet(_clock));

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(m.JoinGuild(guild, "old", "Old", 100, false).Success);
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(m.JoinGuild(guild, "officer", "Officer", 100, false).Success);
        Assert.Equal(ErrorCode.AlreadyMember, m.JoinGuild(guild, "old", "Old", 100, false).Error);

        Assert.True(m.SetRole(guild, "leader", "officer", GuildRole.Officer).Success);
        Assert.Equal(ErrorCode.PermissionDenied, m.Kick(guild, "old", "officer").Error);
        Assert.Equal(ErrorCode.PermissionDenied, m.SetRole(guild, "officer", "old", GuildRole.Officer).Error);

        for (int i = 0; i < 17; i++)
        {
            Assert.True(m.JoinGuild(guild, "p" + i, "P" + i, 10, false).Success);
        }
        Assert.Equal(20, guild.Members.Count);
        Assert.Equal(ErrorCode.GuildFull, m.JoinGuild(guild, "extra", "Extra", 10, false).Error);

        Assert.True(m.Kick(guild, "officer", "p0").Success);
        Assert.False(m.Leave(guild, "leader").Value);
        Assert.Equal(GuildRole.Leader, guild.Find("officer")!.Role);

        guild.IsOpen = false;
        Assert.Equal(ErrorCode.PermissionDenied, m.JoinGuild(guild, "closed", "Closed", 10, false).Error);
        Assert.True(m.JoinGuild(guild, "closed", "Closed", 10, false, invited: true).Success);
    }

    [Fact]
    public void Donations_LevelUpTo20_WithEscalatingCosts()
    {
        var m = Manager();
        var wallet = EconomyFixtures.Wallet(_clock, 10000, 5_000_000);
        var guild = NewGuild(m, wallet);

        Assert.Equal(5, m.GetNextLevelPoints(guild));
        // Coins: 100 per point, odd change is not taken.
        GuildDonationResult first = m.Donate(guild, "leader", wallet, Currency.Coins, 250).Value;
        Assert.False(first.LeveledUp);
        Assert.Equal(200, first.Paid);
        Assert.Equal(2, guild.DonationProgress);
        var second = m.Donate(guild, "leader", wallet, Currency.Coins, 900).Value;
        Assert.Equal(900, second.Paid);
        Assert.True(second.LeveledUp);
        Assert.Equal(6, guild.DonationProgress);
        Assert.Equal(100, m.GetNextLevelPoints(guild));

        // The coin allowance is daily: 5000 per member, then only orbes until tomorrow.
        GuildMember leader = guild.Find("leader");
        Assert.Equal(3900, m.CoinsLeftToday(leader));
        Assert.Equal(3900, m.Donate(guild, "leader", wallet, Currency.Coins, 9000).Value.Paid);
        Assert.Equal(ErrorCode.LimitReached, m.Donate(guild, "leader", wallet, Currency.Coins, 100).Error);
        _clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(5000, m.CoinsLeftToday(leader));

        // One orbe donation covering several levels climbs them all and keeps the rest as progress.
        long progress = guild.DonationProgress;
        int level = guild.Level;
        long need = m.PointsForLevel(level + 1) - progress + m.PointsForLevel(level + 2);
        GuildDonationResult big = m.Donate(guild, "leader", wallet, Currency.Orbes, need + 10).Value;
        Assert.Equal(2, big.LevelsGained);
        Assert.Equal(need + 10, big.Paid);
        Assert.Equal(10, guild.DonationProgress);

        // Level 20 costs follow +25% steps, then +4% per level up to 50.
        Assert.Equal(m.PointsForLevel(21) * 1040 / 1000, m.PointsForLevel(22));
        while (guild.Level < 50)
        {
            Assert.True(m.DonateTechPoints(guild, "leader", wallet, 200000).Success);
        }
        Assert.Equal(ErrorCode.LimitReached, m.Donate(guild, "leader", wallet, 1).Error);
        Assert.Equal(49, guild.TechPointsAvailable);
        Assert.Equal(ErrorCode.NotMember, m.Donate(guild, "stranger", wallet, 1).Error);

        // A maxed guild can complete the whole tech tree, exactly.
        foreach (GuildTechDefinition definition in GuildTechTree.Definitions)
        {
            for (int i = 0; i < definition.MaxRank; i++)
            {
                Assert.True(m.SpendTechPoint(guild, "leader", definition.Tech).Success);
            }
            Assert.False(m.SpendTechPoint(guild, "leader", definition.Tech).Success);
        }
        Assert.Equal(0, guild.TechPointsAvailable);
        Assert.Equal(300, GuildManager.GetTechValue(guild, GuildTech.BossDamage));
    }

    [Fact]
    public void Boss_AttacksDamageDefeatAndProgression()
    {
        var m = Manager();
        var guild = NewGuild(m, EconomyFixtures.Wallet(_clock));
        m.JoinGuild(guild, "m1", "M1", 100, false);
        int week = TimeUtil.WeekIndex(_clock.UtcNow);

        Assert.Equal(60000, m.BossHp(1));
        Assert.Equal(81000, m.BossHp(2));

        for (int i = 0; i < 3; i++)
        {
            Assert.True(m.SubmitBossDamage(guild, "m1", 1000, week).Success);
        }
        Assert.Equal(ErrorCode.LimitReached, m.SubmitBossDamage(guild, "m1", 1000, week).Error);

        m.SubmitBossDamage(guild, "leader", 30000, week);
        var killing = m.SubmitBossDamage(guild, "leader", 30000, week).Value;
        Assert.True(killing.DefeatedNow);
        Assert.Equal(2, killing.RewardedPlayers.Count);
        Assert.Equal(500, killing.RewardPerMember.Coins);
        Assert.Equal(ErrorCode.SessionOver, m.SubmitBossDamage(guild, "leader", 1, week).Error);

        // m1 started an attack before the boss fell: it is not lost.
        long defeatedAt = guild.Boss.DefeatedAtUnixMs;
        Assert.Equal(ErrorCode.SessionOver, m.SubmitBossDamage(guild, "leader", 1, week, defeatedAt + 1).Error);
        GuildMember m1 = guild.Find("m1");
        long before = m1.BossDamageThisWeek;
        var late = m.SubmitBossDamage(guild, "m1", 500, week, defeatedAt - 1000).Value;
        Assert.True(late.DefeatedDuringAttack);
        Assert.False(late.DefeatedNow);
        Assert.Equal(before + late.DamageApplied, m1.BossDamageThisWeek);

        Assert.Equal(2, m.EnsureBossWeek(guild, week + 1).BossIndex);
        Assert.True(m.SubmitBossDamage(guild, "m1", 10, week + 1).Success);
        Assert.Equal(2, m.EnsureBossWeek(guild, week + 2).BossIndex);
    }

    [Fact]
    public void Ranking_AndRewardSplit()
    {
        var m = Manager();
        var guilds = new Dictionary<string, Guild>();
        for (int i = 0; i < 4; i++)
        {
            var g = new Guild { Id = "g" + i, Name = "Guild " + i };
            for (int j = 0; j <= i; j++)
            {
                g.Members.Add(new GuildMember { PlayerId = $"g{i}p{j}", Trophies = 500 });
            }
            guilds[g.Id] = g;
        }

        var ranking = GuildManager.CalculateGuildRanking(guilds.Values);
        Assert.Equal("g3", ranking[0].GuildId);
        Assert.Equal(4, GuildManager.CalculateGuildRank(guilds["g0"], guilds.Values));

        var rewards = m.DistributeRankingRewards(ranking, guilds);
        Assert.Equal(10, rewards.Count);
        Assert.Equal(7500, rewards.First(r => r.GuildId == "g3").Reward.Coins);
        Assert.Equal(12000, rewards.Single(r => r.GuildId == "g0").Reward.Coins);
    }
}

public class FriendsTests
{
    [Fact]
    public void RequestAcceptChallengeCooldown()
    {
        var clock = EconomyFixtures.Clock();
        var friends = new FriendsManager(Fixtures.Balance, clock);
        var alice = new FriendsState();
        var bob = new FriendsState();

        Assert.Equal(ErrorCode.InvalidArgument, friends.SendRequest("alice", alice, "alice", alice).Error);
        Assert.True(friends.SendRequest("alice", alice, "bob", bob).Success);
        Assert.Equal(ErrorCode.DuplicateRequest, friends.SendRequest("alice", alice, "bob", bob).Error);
        Assert.Equal(ErrorCode.NotFound, friends.CanChallenge(alice, "bob"));

        Assert.True(friends.Accept("bob", bob, "alice", alice).Success);
        Assert.Contains("bob", alice.Friends);
        Assert.Empty(alice.Outgoing);

        Assert.True(friends.RecordChallenge(alice, "bob").Success);
        Assert.Equal(ErrorCode.CooldownActive, friends.CanChallenge(alice, "bob"));
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(60000, friends.ChallengeCooldownRemainingMs(alice, "bob"));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(ErrorCode.None, friends.CanChallenge(alice, "bob"));

        Assert.True(friends.Block("bob", bob, "alice", alice).Success);
        Assert.Empty(alice.Friends);
        Assert.Equal(ErrorCode.PermissionDenied, friends.SendRequest("alice", alice, "bob", bob).Error);
    }

    [Fact]
    public void MutualRequests_AutoAccept()
    {
        var friends = new FriendsManager(Fixtures.Balance, EconomyFixtures.Clock());
        var a = new FriendsState();
        var b = new FriendsState();
        friends.SendRequest("a", a, "b", b);
        Assert.True(friends.SendRequest("b", b, "a", a).Success);
        Assert.Contains("a", b.Friends);
    }

    [Fact]
    public void PowerUpSteal_OnlyVip9PlusInFriendlies()
    {
        Assert.False(FriendsManager.RollPowerUpSteal(Fixtures.Balance, VipTier.Vip10, GameMode.PvpRanked, 1));
        int steals = Enumerable.Range(0, 1000).Count(seed => FriendsManager.RollPowerUpSteal(Fixtures.Balance, VipTier.Vip10, GameMode.FriendlyChallenge, (ulong)seed));
        Assert.InRange(steals, 700, 800);
    }
}

public class ChatModeratorTests
{
    [Fact]
    public void Moderation_Rules()
    {
        var clock = EconomyFixtures.Clock();
        var chat = new ChatModerator(Fixtures.Balance, clock, new[] { "noob" });

        var masked = chat.Moderate("p", "you are a fucker");
        Assert.True(masked.Accepted);
        Assert.Equal("you are a ******", masked.Text);

        Assert.Equal(ErrorCode.CooldownActive, chat.Moderate("p", "hello").Error);
        clock.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal("Scunthorpe is nice", chat.Moderate("p", "Scunthorpe is nice").Text);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal("gg ****", chat.Moderate("p", "gg n00b").Text);
        clock.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(ErrorCode.MessageRejected, chat.Moderate("p", "free orbes at www.scam.com").Error);
        Assert.Equal(ErrorCode.MessageRejected, chat.Moderate("p", new string('a', 201)).Error);
        Assert.Equal(ErrorCode.Banned, chat.Moderate("p", "hi", muted: true).Error);
        Assert.Equal(ErrorCode.InvalidArgument, chat.Moderate("p", "   ").Error);

        Assert.True(chat.Moderate("spammer", "boss now").Accepted);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True(chat.Moderate("spammer", "BOSS NOW").Accepted);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(ErrorCode.MessageRejected, chat.Moderate("spammer", "boss now").Error);
    }
}
