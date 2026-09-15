using System;
using System.Collections.Generic;
using System.Linq;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Pvp
{
    public sealed class LeaderboardEntry
    {
        public int Rank { get; set; }

        public string PlayerId { get; set; }

        public string DisplayName { get; set; }

        public int Trophies { get; set; }

        public League League { get; set; }
    }

    public sealed class SeasonRewardEntry
    {
        public string PlayerId { get; set; }

        public League FinalLeague { get; set; }

        /// <summary>Rank inside the league (1-based) if in the top list, else 0.</summary>
        public int RankInLeague { get; set; }

        public int Coins { get; set; }

        public int Orbes { get; set; }

        public int TrophiesBefore { get; set; }

        public int TrophiesAfter { get; set; }
    }

    public sealed class SeasonResetResult
    {
        public int Week { get; set; }

        public List<SeasonRewardEntry> Rewards { get; } = new List<SeasonRewardEntry>();

        /// <summary>Archived top N per league for the records.</summary>
        public Dictionary<League, List<LeaderboardEntry>> ArchivedTop { get; } = new Dictionary<League, List<LeaderboardEntry>>();
    }

    /// <summary>Weekly (Sunday midnight UTC) season close: rank, reward, archive, reset trophies (history kept).</summary>
    public static class SeasonService
    {
        /// <summary>Trophies after the reset. Full = 0 (GDD); Soft (default) = floor + half of the excess.</summary>
        public static int ApplyReset(int trophies, TrophyBalance balance)
        {
            if (balance.ResetPolicy == SeasonResetPolicy.Full)
            {
                return 0;
            }
            if (trophies <= balance.SoftResetFloor)
            {
                return Math.Max(0, trophies);
            }
            return balance.SoftResetFloor + (trophies - balance.SoftResetFloor) / 2;
        }

        public static List<LeaderboardEntry> BuildLeaderboard(IEnumerable<PlayerTrophyRecord> players, TrophyBalance balance, League? league = null, int top = int.MaxValue, Func<string, string> displayName = null)
        {
            var ordered = players
                .Where(p => p != null && (league == null || LeagueTable.GetLeague(p.Trophies, balance) == league.Value))
                .OrderByDescending(p => p.Trophies)
                .ThenBy(p => p.PlayerId, StringComparer.Ordinal)
                .Take(top)
                .ToList();

            var list = new List<LeaderboardEntry>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                list.Add(new LeaderboardEntry
                {
                    Rank = i + 1,
                    PlayerId = ordered[i].PlayerId,
                    DisplayName = displayName?.Invoke(ordered[i].PlayerId),
                    Trophies = ordered[i].Trophies,
                    League = LeagueTable.GetLeague(ordered[i].Trophies, balance)
                });
            }
            return list;
        }

        public static int TopRankOrbes(int rank, TrophyBalance balance, int topCount)
        {
            if (rank <= 0 || rank > topCount)
            {
                return 0;
            }
            if (rank == 1)
            {
                return balance.TopRankOrbes[0];
            }
            return rank <= 10 ? balance.TopRankOrbes[1] : balance.TopRankOrbes[2];
        }

        /// <summary>
        /// Prompt API equivalent of ResetSeasonalData(): mutates the records (trophies, streak, season fields)
        /// and returns the rewards to credit plus the archived leaderboards.
        /// </summary>
        public static SeasonResetResult ResetSeasonalData(IList<PlayerTrophyRecord> players, int closingWeek, TrophyBalance balance)
        {
            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }

            var result = new SeasonResetResult { Week = closingWeek };
            var rankLookup = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (League league in (League[])Enum.GetValues(typeof(League)))
            {
                List<LeaderboardEntry> top = BuildLeaderboard(players, balance, league, balance.TopRankRewardCount);
                result.ArchivedTop[league] = top;
                foreach (LeaderboardEntry e in top)
                {
                    rankLookup[e.PlayerId] = e.Rank;
                }
            }

            foreach (PlayerTrophyRecord p in players)
            {
                if (p == null)
                {
                    continue;
                }

                League final = LeagueTable.GetLeague(p.Trophies, balance);
                rankLookup.TryGetValue(p.PlayerId ?? string.Empty, out int rank);
                bool played = p.History.Any(h => h.Week == closingWeek);

                var reward = new SeasonRewardEntry
                {
                    PlayerId = p.PlayerId,
                    FinalLeague = final,
                    RankInLeague = rank,
                    // Inactive players keep their trophies' reset but get no reward (no farming with idle accounts).
                    Coins = played ? balance.SeasonCoinsByLeague[(int)final] : 0,
                    Orbes = played ? balance.SeasonOrbesByLeague[(int)final] + TopRankOrbes(rank, balance, balance.TopRankRewardCount) : 0,
                    TrophiesBefore = p.Trophies,
                    TrophiesAfter = ApplyReset(p.Trophies, balance)
                };
                result.Rewards.Add(reward);

                p.Trophies = reward.TrophiesAfter;
                p.CurrentWinStreak = 0;
                p.SeasonWeek = closingWeek + 1;
                p.SeasonBestTrophies = p.Trophies;
            }
            return result;
        }
    }
}
