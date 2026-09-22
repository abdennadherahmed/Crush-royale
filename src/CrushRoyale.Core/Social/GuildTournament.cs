using System;
using System.Collections.Generic;

namespace CrushRoyale.Core.Social
{
    /// <summary>
    /// The weekly race between guilds. Everything a member does for the guild during the week - clearing a stage,
    /// winning a duel in the arena, hitting the boss - adds points to one shared score, and the guilds are ranked
    /// against each other on Sunday night.
    ///
    /// A guild needs a real team to enter: below <see cref="GuildTournamentBalance.MinMembers"/> members it still
    /// collects points and still sees them, but it is not ranked and takes no reward. A tournament a lone player can
    /// win is not a tournament, and the rule is what makes people recruit.
    /// </summary>
    public sealed class GuildTournamentState
    {
        /// <summary>Week the points below belong to; a different week means they have not been reset yet.</summary>
        public int Week { get; set; } = -1;

        public long Points { get; set; }

        /// <summary>Rank and score of the last closed week, kept so the guild can see how it did.</summary>
        public int LastRank { get; set; }

        public long LastPoints { get; set; }

        /// <summary>A reward earned last week and not yet handed out.</summary>
        public bool RewardPending { get; set; }
    }

    /// <summary>What one action is worth to the guild's weekly score.</summary>
    public sealed class GuildTournamentBalance
    {
        /// <summary>Members a guild needs before it is ranked at all.</summary>
        public int MinMembers { get; set; } = 5;

        public int PointsPerStageWon { get; set; } = 10;

        /// <summary>A star is the mark of a clean clear, so a three-star run is worth noticeably more.</summary>
        public int PointsPerStar { get; set; } = 5;

        public int PointsPerArenaWin { get; set; } = 30;

        /// <summary>Points per 1000 damage dealt to the guild boss.</summary>
        public int PointsPerBossDamageThousand { get; set; } = 8;

        /// <summary>One member can only bring this much in a week: a whale cannot carry a dead guild alone.</summary>
        public int MaxPointsPerMemberPerWeek { get; set; } = 4000;

        /// <summary>Coins for the guild's rank at the close of the week, best first; beyond the list, nothing.</summary>
        public List<int> RankRewardCoins { get; set; } = new List<int> { 5000, 3000, 2000, 1200, 800, 600, 500, 400, 300, 200 };

        /// <summary>Orbes for the top three.</summary>
        public List<int> RankRewardOrbes { get; set; } = new List<int> { 150, 90, 50 };

        public void Validate()
        {
            Config.GameBalance.Require(MinMembers >= 1, "Guild tournament needs at least one member to be enterable.");
            Config.GameBalance.Require(MaxPointsPerMemberPerWeek > 0, "A member cap of zero would freeze every score.");
            Config.GameBalance.Require(RankRewardCoins.Count > 0, "The tournament must reward at least the winner.");
        }
    }

    public static class GuildTournament
    {
        /// <summary>
        /// Starts a new week when the stored one is stale: the score goes to zero, last week's is kept for display,
        /// and a reward is marked as owed when the guild was ranked.
        /// </summary>
        public static void EnsureWeek(Guild guild, int week)
        {
            if (guild == null)
            {
                return;
            }
            guild.Tournament ??= new GuildTournamentState();
            GuildTournamentState state = guild.Tournament;
            if (state.Week == week)
            {
                return;
            }

            if (state.Week >= 0)
            {
                state.LastPoints = state.Points;
            }
            state.Week = week;
            state.Points = 0;
            foreach (GuildMember member in guild.Members)
            {
                if (member.TournamentWeek != week)
                {
                    member.TournamentWeek = week;
                    member.TournamentPoints = 0;
                }
            }
        }

        /// <summary>A guild is ranked, and can be rewarded, only once it is a real team.</summary>
        public static bool IsRanked(Guild guild, GuildTournamentBalance balance) =>
            guild != null && balance != null && guild.Members.Count >= balance.MinMembers;

        /// <summary>Members still to recruit before the guild enters the race (0 when it is already in).</summary>
        public static int MembersMissing(Guild guild, GuildTournamentBalance balance) =>
            guild == null || balance == null ? 0 : Math.Max(0, balance.MinMembers - guild.Members.Count);

        /// <summary>
        /// Credits a member's contribution and returns what was actually added, which is less than asked once the
        /// member has hit their weekly cap.
        /// </summary>
        public static long AddPoints(Guild guild, string playerId, long points, int week, GuildTournamentBalance balance)
        {
            if (guild == null || balance == null || points <= 0)
            {
                return 0;
            }
            EnsureWeek(guild, week);
            GuildMember member = guild.Members.Find(m => m.PlayerId == playerId);
            if (member == null)
            {
                return 0;
            }
            if (member.TournamentWeek != week)
            {
                member.TournamentWeek = week;
                member.TournamentPoints = 0;
            }

            long room = Math.Max(0, balance.MaxPointsPerMemberPerWeek - member.TournamentPoints);
            long credited = Math.Min(points, room);
            if (credited <= 0)
            {
                return 0;
            }
            member.TournamentPoints += credited;
            guild.Tournament.Points += credited;
            return credited;
        }

        /// <summary>Points a finished story stage is worth, stars included.</summary>
        public static long ForStage(int stars, GuildTournamentBalance balance) =>
            balance == null ? 0 : balance.PointsPerStageWon + (long)Math.Max(0, stars) * balance.PointsPerStar;

        public static long ForArenaWin(GuildTournamentBalance balance) => balance?.PointsPerArenaWin ?? 0;

        public static long ForBossDamage(long damage, GuildTournamentBalance balance) =>
            balance == null || damage <= 0 ? 0 : damage * balance.PointsPerBossDamageThousand / 1000;

        /// <summary>Coins and orbes owed to a guild that finished the week at this rank (1-based); zero outside the table.</summary>
        public static (int Coins, int Orbes) RewardFor(int rank, GuildTournamentBalance balance)
        {
            if (balance == null || rank < 1)
            {
                return (0, 0);
            }
            int coins = rank <= balance.RankRewardCoins.Count ? balance.RankRewardCoins[rank - 1] : 0;
            int orbes = rank <= balance.RankRewardOrbes.Count ? balance.RankRewardOrbes[rank - 1] : 0;
            return (coins, orbes);
        }
    }
}
