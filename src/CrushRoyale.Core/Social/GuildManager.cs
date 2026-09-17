using System;
using System.Collections.Generic;
using System.Linq;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;

namespace CrushRoyale.Core.Social
{
    public enum GuildRole : byte
    {
        Member = 0,
        Officer = 1,
        Leader = 2
    }

    public enum GuildTech : byte
    {
        CoinBonus = 0,
        BossDamage = 1,
        LifeRecharge = 2,
        MaxLives = 3,
        PowerUpDiscount = 4,
        BattlePassXp = 5
    }

    public sealed class GuildTechDefinition
    {
        public GuildTech Tech { get; internal set; }

        public int MaxRank { get; internal set; }

        /// <summary>Permille per rank (MaxLives: lives per rank).</summary>
        public int ValuePerRank { get; internal set; }
    }

    public static class GuildTechTree
    {
        /// <summary>21 ranks in total for 19 tech points: guilds must choose.</summary>
        public static readonly IReadOnlyList<GuildTechDefinition> Definitions = new List<GuildTechDefinition>
        {
            new GuildTechDefinition { Tech = GuildTech.CoinBonus, MaxRank = 5, ValuePerRank = 20 },
            new GuildTechDefinition { Tech = GuildTech.BossDamage, MaxRank = 5, ValuePerRank = 30 },
            new GuildTechDefinition { Tech = GuildTech.LifeRecharge, MaxRank = 3, ValuePerRank = 50 },
            new GuildTechDefinition { Tech = GuildTech.MaxLives, MaxRank = 2, ValuePerRank = 1 },
            new GuildTechDefinition { Tech = GuildTech.PowerUpDiscount, MaxRank = 3, ValuePerRank = 20 },
            new GuildTechDefinition { Tech = GuildTech.BattlePassXp, MaxRank = 3, ValuePerRank = 50 }
        };

        public static GuildTechDefinition Get(GuildTech tech) => Definitions.First(d => d.Tech == tech);
    }

    public sealed class GuildMember
    {
        public string PlayerId { get; set; }

        public string DisplayName { get; set; }

        public GuildRole Role { get; set; }

        public long JoinedAtUnixMs { get; set; }

        public long DonatedCoins { get; set; }

        public long DonatedOrbes { get; set; }

        /// <summary>Day index of <see cref="CoinsDonatedToday"/> (the coin allowance resets daily).</summary>
        public int CoinDonationDay { get; set; } = -1;

        public long CoinsDonatedToday { get; set; }

        public int Trophies { get; set; }

        public int BossWeek { get; set; } = -1;

        public int BossAttacksThisWeek { get; set; }

        public long BossDamageThisWeek { get; set; }
    }

    public sealed class GuildBossState
    {
        public int Week { get; set; }

        public int BossIndex { get; set; } = 1;

        public long MaxHp { get; set; }

        public long Damage { get; set; }

        public bool Defeated { get; set; }

        public long DefeatedAtUnixMs { get; set; }

        public long RemainingHp => Math.Max(0, MaxHp - Damage);
    }

    public sealed class Guild
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; } = string.Empty;

        public int Level { get; set; } = 1;

        public long DonationProgress { get; set; }

        public int TechPointsAvailable { get; set; }

        public Dictionary<GuildTech, int> TechRanks { get; set; } = new Dictionary<GuildTech, int>();

        public List<GuildMember> Members { get; set; } = new List<GuildMember>();

        public long CreatedAtUnixMs { get; set; }

        /// <summary>Open guilds accept joins directly; closed guilds need an invitation (server).</summary>
        public bool IsOpen { get; set; } = true;

        public int MinTrophies { get; set; }

        public GuildBossState Boss { get; set; }

        public long TotalTrophies => Members.Sum(m => (long)m.Trophies);

        public GuildMember Leader => Members.FirstOrDefault(m => m.Role == GuildRole.Leader);

        public GuildMember Find(string playerId) => Members.FirstOrDefault(m => m.PlayerId == playerId);

        public int TechRank(GuildTech tech) => TechRanks.TryGetValue(tech, out int r) ? r : 0;
    }

    public sealed class GuildDonationResult
    {
        public long Paid { get; internal set; }

        public Currency Currency { get; internal set; }

        public bool LeveledUp { get; internal set; }

        /// <summary>How many levels this donation climbed.</summary>
        public int LevelsGained { get; internal set; }

        /// <summary>Guild points added.</summary>
        public long Points { get; internal set; }

        public long CoinsLeftToday { get; internal set; }

        public int NewLevel { get; internal set; }
    }

    public sealed class BossAttackResult
    {
        public long DamageApplied { get; internal set; }

        public int AttacksLeft { get; internal set; }

        public bool DefeatedNow { get; internal set; }

        /// <summary>
        /// The boss fell to other members while this attack was being played: the damage still counts on the damage
        /// board, the attack is not consumed, and the defeat reward is shared with everyone anyway.
        /// </summary>
        public bool DefeatedDuringAttack { get; internal set; }

        /// <summary>Players rewarded (all current members) when the boss falls.</summary>
        public List<string> RewardedPlayers { get; } = new List<string>();

        public RewardData RewardPerMember { get; internal set; }
    }

    public sealed class GuildRankingEntry
    {
        public int Rank { get; internal set; }

        public string GuildId { get; internal set; }

        public string Name { get; internal set; }

        public long TotalTrophies { get; internal set; }

        public int Members { get; internal set; }

        public int Level { get; internal set; }
    }

    public sealed class GuildMemberReward
    {
        public string GuildId { get; internal set; }

        public string PlayerId { get; internal set; }

        public int Rank { get; internal set; }

        public RewardData Reward { get; internal set; }
    }

    /// <summary>
    /// Task 11: guild rules. Creation, membership and roles, donations and levels, tech tree, weekly boss,
    /// weekly ranking and reward distribution. Persistence and name uniqueness are handled by the server.
    /// </summary>
    public sealed class GuildManager
    {
        private readonly GameBalance _balance;
        private readonly IClock _clock;

        public GuildManager(GameBalance balance, IClock clock)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        private long Now => TimeUtil.ToUnixMs(_clock.UtcNow);

        public ErrorCode ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return ErrorCode.NameInvalid;
            }
            string trimmed = name.Trim();
            if (trimmed.Length < _balance.Guild.MinNameLength || trimmed.Length > _balance.Guild.MaxNameLength || trimmed.Contains("  "))
            {
                return ErrorCode.NameInvalid;
            }
            foreach (char c in trimmed)
            {
                if (!char.IsLetterOrDigit(c) && c != ' ' && c != '_' && c != '-')
                {
                    return ErrorCode.NameInvalid;
                }
            }
            return ChatModerator.ContainsProfanity(trimmed) ? ErrorCode.NameInvalid : ErrorCode.None;
        }

        /// <summary>Prompt API. Costs 100 coins; requires the guild unlock stage and no current guild.</summary>
        public OperationResult<Guild> CreateGuild(string guildId, string name, string leaderId, string leaderName, int leaderTrophies, IWallet wallet, int highestUnlockedStage, bool alreadyInGuild)
        {
            if (wallet == null)
            {
                throw new ArgumentNullException(nameof(wallet));
            }
            if (string.IsNullOrEmpty(guildId) || string.IsNullOrEmpty(leaderId))
            {
                return OperationResult<Guild>.Fail(ErrorCode.InvalidArgument);
            }
            if (highestUnlockedStage <= _balance.Story.UnlockGuildsStage)
            {
                return OperationResult<Guild>.Fail(ErrorCode.FeatureLocked);
            }
            if (alreadyInGuild)
            {
                return OperationResult<Guild>.Fail(ErrorCode.AlreadyMember);
            }
            ErrorCode nameError = ValidateName(name);
            if (nameError != ErrorCode.None)
            {
                return OperationResult<Guild>.Fail(nameError);
            }

            OperationResult pay = wallet.Debit(Currency.Coins, _balance.Guild.CreateCostCoins, TransactionReason.GuildCreation, guildId);
            if (!pay.Success)
            {
                return OperationResult<Guild>.Fail(pay.Error);
            }

            var guild = new Guild { Id = guildId, Name = name.Trim(), CreatedAtUnixMs = Now };
            guild.Members.Add(new GuildMember { PlayerId = leaderId, DisplayName = leaderName, Role = GuildRole.Leader, JoinedAtUnixMs = Now, Trophies = leaderTrophies });
            return OperationResult<Guild>.Ok(guild);
        }

        /// <summary>Prompt API.</summary>
        public OperationResult JoinGuild(Guild guild, string playerId, string displayName, int trophies, bool alreadyInGuild, bool invited = false)
        {
            if (guild == null)
            {
                throw new ArgumentNullException(nameof(guild));
            }
            if (alreadyInGuild || guild.Find(playerId) != null)
            {
                return OperationResult.Fail(ErrorCode.AlreadyMember);
            }
            if (guild.Members.Count >= _balance.Guild.MaxMembers)
            {
                return OperationResult.Fail(ErrorCode.GuildFull);
            }
            if (!guild.IsOpen && !invited)
            {
                return OperationResult.Fail(ErrorCode.PermissionDenied);
            }
            if (trophies < guild.MinTrophies && !invited)
            {
                return OperationResult.Fail(ErrorCode.PermissionDenied, "Not enough trophies.");
            }

            guild.Members.Add(new GuildMember { PlayerId = playerId, DisplayName = displayName, Role = GuildRole.Member, JoinedAtUnixMs = Now, Trophies = trophies });
            return OperationResult.Ok();
        }

        /// <summary>Leaves the guild. A leaving leader hands over to the most senior officer, else the most senior member. Returns true in Value when the guild is now empty (disband).</summary>
        public OperationResult<bool> Leave(Guild guild, string playerId)
        {
            GuildMember member = guild?.Find(playerId);
            if (member == null)
            {
                return OperationResult<bool>.Fail(ErrorCode.NotMember);
            }

            guild.Members.Remove(member);
            if (guild.Members.Count == 0)
            {
                return OperationResult<bool>.Ok(true);
            }
            if (member.Role == GuildRole.Leader)
            {
                GuildMember successor = guild.Members
                    .OrderByDescending(m => m.Role)
                    .ThenBy(m => m.JoinedAtUnixMs)
                    .First();
                successor.Role = GuildRole.Leader;
            }
            return OperationResult<bool>.Ok(false);
        }

        public OperationResult Kick(Guild guild, string actorId, string targetId)
        {
            GuildMember actor = guild?.Find(actorId);
            GuildMember target = guild?.Find(targetId);
            if (actor == null || target == null)
            {
                return OperationResult.Fail(ErrorCode.NotMember);
            }
            if (actor.Role < GuildRole.Officer || actor.Role <= target.Role)
            {
                return OperationResult.Fail(ErrorCode.PermissionDenied);
            }
            guild.Members.Remove(target);
            return OperationResult.Ok();
        }

        public OperationResult SetRole(Guild guild, string actorId, string targetId, GuildRole role)
        {
            GuildMember actor = guild?.Find(actorId);
            GuildMember target = guild?.Find(targetId);
            if (actor == null || target == null)
            {
                return OperationResult.Fail(ErrorCode.NotMember);
            }
            if (actor.Role != GuildRole.Leader || actorId == targetId)
            {
                return OperationResult.Fail(ErrorCode.PermissionDenied);
            }

            if (role == GuildRole.Leader)
            {
                actor.Role = GuildRole.Officer;
                target.Role = GuildRole.Leader;
                return OperationResult.Ok();
            }
            if (role == GuildRole.Officer && target.Role != GuildRole.Officer && guild.Members.Count(m => m.Role == GuildRole.Officer) >= _balance.Guild.MaxOfficers)
            {
                return OperationResult.Fail(ErrorCode.LimitReached);
            }
            target.Role = role;
            return OperationResult.Ok();
        }

        /// <summary>Guild points needed for the next level: 5 for level 2, then 100, 125, 156... (+25% each).</summary>
        public long GetNextLevelPoints(Guild guild) => PointsForLevel(guild.Level + 1);

        /// <summary>Guild points needed to reach <paramref name="next"/>.</summary>
        public long PointsForLevel(int next)
        {
            if (next <= 2)
            {
                return _balance.Guild.Level2CostPoints;
            }
            long cost = _balance.Guild.OrbeLevelBaseCost;
            for (int level = 3; level < next; level++)
            {
                cost = cost * _balance.Guild.OrbeLevelEscalationPermille / 1000;
            }
            return cost;
        }

        /// <summary>Coins this member can still donate today (the coin allowance resets every day, orbes are unlimited).</summary>
        public long CoinsLeftToday(GuildMember member)
        {
            if (member == null)
            {
                return 0;
            }
            long used = member.CoinDonationDay == TimeUtil.DayIndex(_clock.UtcNow) ? member.CoinsDonatedToday : 0;
            return Math.Max(0, _balance.Guild.DailyCoinDonationCap - used);
        }

        /// <summary>Orbe donation (kept for older callers).</summary>
        public OperationResult<GuildDonationResult> Donate(Guild guild, string playerId, IWallet wallet, long amount) =>
            Donate(guild, playerId, wallet, Currency.Orbes, amount);

        /// <summary>
        /// Contributes toward the guild levels. Coins (a daily allowance per member) and orbes (unlimited) both turn into
        /// guild points: <see cref="GuildBalance.CoinsPerPoint"/> coins or one orbe per point. A big donation climbs as many
        /// levels as it pays for; only what is needed is taken (nothing is taken past the max level).
        /// </summary>
        public OperationResult<GuildDonationResult> Donate(Guild guild, string playerId, IWallet wallet, Currency currency, long amount)
        {
            GuildMember member = guild?.Find(playerId);
            if (member == null)
            {
                return OperationResult<GuildDonationResult>.Fail(ErrorCode.NotMember);
            }
            if (wallet == null)
            {
                throw new ArgumentNullException(nameof(wallet));
            }
            if (amount <= 0 || (currency != Currency.Coins && currency != Currency.Orbes))
            {
                return OperationResult<GuildDonationResult>.Fail(ErrorCode.InvalidArgument);
            }
            if (guild.Level >= _balance.Guild.MaxLevel)
            {
                return OperationResult<GuildDonationResult>.Fail(ErrorCode.LimitReached);
            }

            int today = TimeUtil.DayIndex(_clock.UtcNow);
            long unit = currency == Currency.Coins ? Math.Max(1, _balance.Guild.CoinsPerPoint) : 1;
            if (currency == Currency.Coins)
            {
                long left = CoinsLeftToday(member);
                if (left < unit)
                {
                    return OperationResult<GuildDonationResult>.Fail(ErrorCode.LimitReached, "Daily coin donation allowance used.");
                }
                amount = Math.Min(amount, left);
            }
            long points = amount / unit;
            if (points <= 0)
            {
                return OperationResult<GuildDonationResult>.Fail(ErrorCode.InvalidArgument, "Donation too small.");
            }

            int level = guild.Level;
            long progress = Math.Max(0, guild.DonationProgress);
            long used = 0;
            int gained = 0;
            while (level < _balance.Guild.MaxLevel)
            {
                long cost = PointsForLevel(level + 1);
                if (progress >= cost)
                {
                    progress -= cost;
                    level++;
                    gained++;
                    continue;
                }
                if (used >= points)
                {
                    break;
                }
                long part = Math.Min(points - used, cost - progress);
                used += part;
                progress += part;
            }
            if (level >= _balance.Guild.MaxLevel)
            {
                progress = 0;
            }

            long pay = used * unit;
            if (pay > 0)
            {
                OperationResult debit = wallet.Debit(currency, pay, TransactionReason.GuildDonation, guild.Id);
                if (!debit.Success)
                {
                    return OperationResult<GuildDonationResult>.Fail(debit.Error);
                }
            }
            if (currency == Currency.Coins)
            {
                if (member.CoinDonationDay != today)
                {
                    member.CoinDonationDay = today;
                    member.CoinsDonatedToday = 0;
                }
                member.CoinsDonatedToday += pay;
                member.DonatedCoins += pay;
            }
            else
            {
                member.DonatedOrbes += pay;
            }

            guild.Level = level;
            guild.DonationProgress = progress;
            guild.TechPointsAvailable += gained * _balance.Guild.TechPointsPerLevel;
            return OperationResult<GuildDonationResult>.Ok(new GuildDonationResult
            {
                Paid = pay,
                Currency = currency,
                Points = used,
                LeveledUp = gained > 0,
                LevelsGained = gained,
                NewLevel = level,
                CoinsLeftToday = CoinsLeftToday(member)
            });
        }

        /// <summary>Prompt API name: donate toward the next level/tech point.</summary>
        public OperationResult<GuildDonationResult> DonateTechPoints(Guild guild, string playerId, IWallet wallet, int amount) => Donate(guild, playerId, wallet, Currency.Orbes, amount);

        public OperationResult SpendTechPoint(Guild guild, string actorId, GuildTech tech)
        {
            GuildMember actor = guild?.Find(actorId);
            if (actor == null)
            {
                return OperationResult.Fail(ErrorCode.NotMember);
            }
            if (actor.Role < GuildRole.Officer)
            {
                return OperationResult.Fail(ErrorCode.PermissionDenied);
            }
            if (guild.TechPointsAvailable <= 0)
            {
                return OperationResult.Fail(ErrorCode.NotEnoughItems);
            }
            GuildTechDefinition def = GuildTechTree.Get(tech);
            if (guild.TechRank(tech) >= def.MaxRank)
            {
                return OperationResult.Fail(ErrorCode.LimitReached);
            }
            guild.TechRanks[tech] = guild.TechRank(tech) + 1;
            guild.TechPointsAvailable--;
            return OperationResult.Ok();
        }

        public static int GetTechValue(Guild guild, GuildTech tech) => guild == null ? 0 : guild.TechRank(tech) * GuildTechTree.Get(tech).ValuePerRank;

        // ------------------------------------------------------------------ boss

        public long BossHp(int bossIndex)
        {
            long hp = _balance.Guild.BossBaseHp;
            for (int i = 1; i < bossIndex; i++)
            {
                hp = hp * _balance.Guild.BossHpGrowthPermille / 1000;
            }
            return hp;
        }

        public RewardData BossRewardPerMember(int bossIndex)
        {
            GuildBalance g = _balance.Guild;
            long coins = g.BossBaseRewardCoins * (1000L + g.BossRewardCoinsGrowthPermille * (bossIndex - 1)) / 1000;
            return RewardData.FromCurrency(coins, g.BossBaseRewardOrbes + bossIndex);
        }

        /// <summary>Starts a new weekly boss if needed: next boss if the previous one fell, else the same boss at full HP.</summary>
        public GuildBossState EnsureBossWeek(Guild guild, int week)
        {
            if (guild.Boss != null && guild.Boss.Week == week)
            {
                return guild.Boss;
            }
            int index = guild.Boss == null ? 1 : (guild.Boss.Defeated ? guild.Boss.BossIndex + 1 : guild.Boss.BossIndex);
            guild.Boss = new GuildBossState { Week = week, BossIndex = index, MaxHp = BossHp(index) };
            return guild.Boss;
        }

        /// <summary>Records one attack (a guild-boss match score, already validated by replay). 3 attacks per member per week.</summary>
        /// <summary>Applies an attack. <c>attackStartedAtUnixMs</c> (0 = unknown): an attack started before the boss fell is never lost.</summary>
        public OperationResult<BossAttackResult> SubmitBossDamage(Guild guild, string playerId, long score, int week, long attackStartedAtUnixMs = 0)
        {
            GuildMember member = guild?.Find(playerId);
            if (member == null)
            {
                return OperationResult<BossAttackResult>.Fail(ErrorCode.NotMember);
            }
            if (score < 0)
            {
                return OperationResult<BossAttackResult>.Fail(ErrorCode.InvalidArgument);
            }

            GuildBossState boss = EnsureBossWeek(guild, week);
            if (member.BossWeek != week)
            {
                member.BossWeek = week;
                member.BossAttacksThisWeek = 0;
                member.BossDamageThisWeek = 0;
            }
            if (boss.Defeated)
            {
                if (attackStartedAtUnixMs <= 0 || attackStartedAtUnixMs > boss.DefeatedAtUnixMs)
                {
                    return OperationResult<BossAttackResult>.Fail(ErrorCode.SessionOver, "Boss already defeated this week.");
                }
                long late = score * (1000 + GetTechValue(guild, GuildTech.BossDamage)) / 1000;
                member.BossDamageThisWeek += late;
                return OperationResult<BossAttackResult>.Ok(new BossAttackResult
                {
                    DamageApplied = late,
                    AttacksLeft = _balance.Guild.BossAttacksPerMemberPerWeek - member.BossAttacksThisWeek,
                    DefeatedDuringAttack = true
                });
            }
            if (member.BossAttacksThisWeek >= _balance.Guild.BossAttacksPerMemberPerWeek)
            {
                return OperationResult<BossAttackResult>.Fail(ErrorCode.LimitReached);
            }

            long damage = score * (1000 + GetTechValue(guild, GuildTech.BossDamage)) / 1000;
            boss.Damage += damage;
            member.BossAttacksThisWeek++;
            member.BossDamageThisWeek += damage;

            var result = new BossAttackResult
            {
                DamageApplied = damage,
                AttacksLeft = _balance.Guild.BossAttacksPerMemberPerWeek - member.BossAttacksThisWeek
            };
            if (boss.Damage >= boss.MaxHp)
            {
                boss.Defeated = true;
                boss.DefeatedAtUnixMs = Now;
                result.DefeatedNow = true;
                result.RewardPerMember = BossRewardPerMember(boss.BossIndex);
                result.RewardedPlayers.AddRange(guild.Members.Select(m => m.PlayerId));
            }
            return OperationResult<BossAttackResult>.Ok(result);
        }

        // ------------------------------------------------------------------ ranking

        public static List<GuildRankingEntry> CalculateGuildRanking(IEnumerable<Guild> guilds)
        {
            var ordered = guilds.Where(g => g != null && g.Members.Count > 0)
                .OrderByDescending(g => g.TotalTrophies)
                .ThenByDescending(g => g.Level)
                .ThenBy(g => g.Id, StringComparer.Ordinal)
                .ToList();

            var list = new List<GuildRankingEntry>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                list.Add(new GuildRankingEntry { Rank = i + 1, GuildId = ordered[i].Id, Name = ordered[i].Name, TotalTrophies = ordered[i].TotalTrophies, Members = ordered[i].Members.Count, Level = ordered[i].Level });
            }
            return list;
        }

        /// <summary>Prompt API: 1-based rank, 0 if unranked.</summary>
        public static int CalculateGuildRank(Guild guild, IEnumerable<Guild> allGuilds)
        {
            GuildRankingEntry entry = CalculateGuildRanking(allGuilds).FirstOrDefault(e => e.GuildId == guild?.Id);
            return entry?.Rank ?? 0;
        }

        /// <summary>Weekly rewards: top 100 guilds, each pool split evenly between current members.</summary>
        public List<GuildMemberReward> DistributeRankingRewards(IList<GuildRankingEntry> ranking, IDictionary<string, Guild> guilds)
        {
            var rewards = new List<GuildMemberReward>();
            foreach (GuildRankingEntry entry in ranking)
            {
                int bucket = entry.Rank == 1 ? 0 : entry.Rank <= 3 ? 1 : entry.Rank <= 10 ? 2 : entry.Rank <= 50 ? 3 : entry.Rank <= 100 ? 4 : -1;
                if (bucket < 0 || !guilds.TryGetValue(entry.GuildId, out Guild guild) || guild.Members.Count == 0)
                {
                    continue;
                }

                int n = guild.Members.Count;
                long coins = Math.Max(1, _balance.Guild.RankingPoolCoins[bucket] / n);
                long orbes = Math.Max(1, _balance.Guild.RankingPoolOrbes[bucket] / n);
                foreach (GuildMember m in guild.Members)
                {
                    rewards.Add(new GuildMemberReward { GuildId = guild.Id, PlayerId = m.PlayerId, Rank = entry.Rank, Reward = RewardData.FromCurrency(coins, orbes) });
                }
            }
            return rewards;
        }
    }
}
