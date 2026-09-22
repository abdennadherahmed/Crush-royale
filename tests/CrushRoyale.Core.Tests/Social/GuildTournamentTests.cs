using System.Collections.Generic;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Social;
using Xunit;

namespace CrushRoyale.Core.Tests.Social;

/// <summary>The weekly race between guilds, and the rule that a guild needs a real team before it can enter it.</summary>
public sealed class GuildTournamentTests
{
    private static readonly GuildTournamentBalance Balance = GameBalance.CreateDefault().Guild.Tournament;

    private static Guild GuildWith(int members)
    {
        var guild = new Guild { Id = "g1", Name = "Winners" };
        for (int i = 0; i < members; i++)
        {
            guild.Members.Add(new GuildMember { PlayerId = "p" + i, DisplayName = "Player " + i });
        }
        return guild;
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(12, true)]
    public void AGuildIsRankedOnlyWithEnoughMembers(int members, bool ranked)
    {
        Assert.Equal(ranked, GuildTournament.IsRanked(GuildWith(members), Balance));
    }

    [Fact]
    public void AGuildTooSmallStillCollectsPoints_SoRecruitingPaysOffImmediately()
    {
        Guild guild = GuildWith(3);
        GuildTournament.AddPoints(guild, "p0", 250, week: 7, Balance);

        Assert.Equal(250, guild.Tournament.Points);
        Assert.False(GuildTournament.IsRanked(guild, Balance));
        Assert.Equal(2, GuildTournament.MembersMissing(guild, Balance));

        guild.Members.Add(new GuildMember { PlayerId = "p3" });
        guild.Members.Add(new GuildMember { PlayerId = "p4" });
        Assert.True(GuildTournament.IsRanked(guild, Balance));
        Assert.Equal(0, GuildTournament.MembersMissing(guild, Balance));
        Assert.Equal(250, guild.Tournament.Points);
    }

    [Fact]
    public void PointsFromEveryMemberAddUpToOneGuildScore()
    {
        Guild guild = GuildWith(5);
        GuildTournament.AddPoints(guild, "p0", 100, 7, Balance);
        GuildTournament.AddPoints(guild, "p1", 60, 7, Balance);
        GuildTournament.AddPoints(guild, "p0", 40, 7, Balance);

        Assert.Equal(200, guild.Tournament.Points);
        Assert.Equal(140, guild.Members[0].TournamentPoints);
        Assert.Equal(60, guild.Members[1].TournamentPoints);
    }

    [Fact]
    public void OneMemberCannotCarryTheWholeGuild()
    {
        Guild guild = GuildWith(5);
        long credited = GuildTournament.AddPoints(guild, "p0", Balance.MaxPointsPerMemberPerWeek + 5000, 7, Balance);

        Assert.Equal(Balance.MaxPointsPerMemberPerWeek, credited);
        Assert.Equal(Balance.MaxPointsPerMemberPerWeek, guild.Tournament.Points);
        Assert.Equal(0, GuildTournament.AddPoints(guild, "p0", 100, 7, Balance));
    }

    [Fact]
    public void ANewWeekResetsTheScoreAndRemembersTheLastOne()
    {
        Guild guild = GuildWith(5);
        GuildTournament.AddPoints(guild, "p0", 900, 7, Balance);
        Assert.Equal(900, guild.Tournament.Points);

        GuildTournament.EnsureWeek(guild, 8);
        Assert.Equal(0, guild.Tournament.Points);
        Assert.Equal(900, guild.Tournament.LastPoints);
        Assert.Equal(0, guild.Members[0].TournamentPoints);
    }

    [Fact]
    public void PointsFromSomeoneWhoLeftTheGuildAreIgnored()
    {
        Guild guild = GuildWith(5);
        Assert.Equal(0, GuildTournament.AddPoints(guild, "stranger", 500, 7, Balance));
        Assert.Equal(0, guild.Tournament.Points);
    }

    [Fact]
    public void AThreeStarClearIsWorthMoreThanABareWin()
    {
        Assert.True(GuildTournament.ForStage(3, Balance) > GuildTournament.ForStage(1, Balance));
        Assert.Equal(Balance.PointsPerStageWon, GuildTournament.ForStage(0, Balance));
    }

    [Fact]
    public void BossDamageConvertsPerThousand()
    {
        Assert.Equal(Balance.PointsPerBossDamageThousand, GuildTournament.ForBossDamage(1000, Balance));
        Assert.Equal(0, GuildTournament.ForBossDamage(0, Balance));
    }

    [Fact]
    public void RewardsFallOffWithRankAndStopOutsideTheTable()
    {
        (int firstCoins, int firstOrbes) = GuildTournament.RewardFor(1, Balance);
        (int secondCoins, _) = GuildTournament.RewardFor(2, Balance);
        (int farCoins, int farOrbes) = GuildTournament.RewardFor(500, Balance);

        Assert.True(firstCoins > secondCoins);
        Assert.True(firstOrbes > 0);
        Assert.Equal(0, farCoins);
        Assert.Equal(0, farOrbes);
    }

    /// <summary>The rule the owner asked for, stated once so no future change can quietly drop it.</summary>
    [Fact]
    public void TheEntryRuleIsFiveMembers()
    {
        Assert.Equal(5, Balance.MinMembers);
    }
}
