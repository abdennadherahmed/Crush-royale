using System.Collections.Generic;

namespace CrushRoyale.Contracts
{
    /// <summary>Route table shared by server and client. Every route is versioned under /v1.</summary>
    public static class ApiRoutes
    {
        public const string Prefix = "/v1";

        public const string Health = "/health";

        public const string Login = Prefix + "/auth/login";
        public const string Config = Prefix + "/config";
        public const string Me = Prefix + "/player/me";
        public const string Hero = Prefix + "/player/me/hero";
        public const string PlayerStats = Prefix + "/player/{id}/stats";
        public const string PlayerSearch = Prefix + "/players/search";

        public const string StageData = Prefix + "/story/{stageId}/data";
        public const string StageStart = Prefix + "/story/{stageId}/start";
        public const string StageContinue = Prefix + "/story/matches/{matchId}/continue";
        public const string StageComplete = Prefix + "/story/matches/{matchId}/complete";
        public const string StoryChoice = Prefix + "/story/choices";
        public const string StoryEventSeen = Prefix + "/story/events/{eventId}/seen";

        public const string LivesBuy = Prefix + "/stamina/buy";
        public const string LivesVipClaim = Prefix + "/stamina/vip-life";
        public const string Shop = Prefix + "/shop";
        public const string ShopRefresh = Prefix + "/shop/refresh";
        public const string ShopPurchase = Prefix + "/shop/purchase";
        public const string IapPrecheck = Prefix + "/iap/precheck";
        public const string IapValidate = Prefix + "/iap/validate";
        public const string Equip = Prefix + "/inventory/equip";
        public const string RewardedAd = Prefix + "/ads/rewarded";
        public const string PetSummon = Prefix + "/pets/summon";
        public const string PetEquip = Prefix + "/pets/equip";
        public const string PetAwaken = Prefix + "/pets/awaken";

        public const string Matchmaking = Prefix + "/pvp/matchmaking";
        public const string PvpMatch = Prefix + "/pvp/match/{matchId}";
        public const string PvpRecord = Prefix + "/pvp/match/record";
        public const string LeaderboardWeekly = Prefix + "/leaderboard/weekly";
        public const string LeaderboardGuilds = Prefix + "/leaderboard/guilds";

        public const string Friends = Prefix + "/friends";
        public const string FriendRequest = Prefix + "/friends/request";
        public const string FriendAccept = Prefix + "/friends/accept";
        public const string FriendDecline = Prefix + "/friends/decline";
        public const string FriendRemove = Prefix + "/friends/remove";
        public const string FriendBlock = Prefix + "/friends/block";
        public const string FriendChallenge = Prefix + "/friends/{friendId}/challenge";
        public const string WorldMap = Prefix + "/friends/map";

        public const string GuildCreate = Prefix + "/guild/create";
        public const string GuildMine = Prefix + "/guild/me";
        public const string GuildById = Prefix + "/guild/{guildId}";
        public const string GuildSearch = Prefix + "/guild/search";
        public const string GuildJoin = Prefix + "/guild/{guildId}/join";
        public const string GuildLeave = Prefix + "/guild/leave";
        public const string GuildKick = Prefix + "/guild/kick";
        public const string GuildRole = Prefix + "/guild/role";
        public const string GuildInvite = Prefix + "/guild/invite";
        public const string GuildSettings = Prefix + "/guild/settings";
        public const string GuildDonate = Prefix + "/guild/donate";
        public const string GuildTech = Prefix + "/guild/tech";
        public const string GuildBossStart = Prefix + "/guild/boss/start";
        public const string GuildBossDamage = Prefix + "/guild/boss/damage";
        public const string GuildChat = Prefix + "/guild/chat";

        public const string Achievements = Prefix + "/achievements";
        public const string AchievementUnlock = Prefix + "/achievement/unlock";
        public const string Quests = Prefix + "/quests/daily";
        public const string QuestClaim = Prefix + "/quests/{questId}/claim";
        public const string LoginBonus = Prefix + "/login-bonus/claim";
        public const string BattlePass = Prefix + "/battlepass";
        public const string BattlePassClaim = Prefix + "/battlepass/claim";

        public const string ReportCheat = Prefix + "/admin/report/cheat";
        public const string AdminFlags = Prefix + "/admin/flags";
        public const string AdminFlagReview = Prefix + "/admin/flags/{flagId}/review";
        public const string AdminGrant = Prefix + "/admin/grant";
        public const string AdminBalance = Prefix + "/admin/balance";
        public const string AdminSeasonReset = Prefix + "/admin/jobs/weekly-reset";

        public static string Fill(string route, string name, object value) => route.Replace("{" + name + "}", System.Uri.EscapeDataString(value.ToString()));
    }

    /// <summary>Error body of every non-2xx response.</summary>
    public sealed class ApiErrorDto
    {
        /// <summary>Name of CrushRoyale.Core.Common.ErrorCode.</summary>
        public string Code { get; set; }

        public string Message { get; set; }
    }

    public sealed class WalletDto
    {
        public long Coins { get; set; }

        public long Orbes { get; set; }
    }

    public sealed class LivesDto
    {
        public int Lives { get; set; }

        public int MaxRegen { get; set; }

        public float RechargeSeconds { get; set; }

        public int FreeContinues { get; set; }

        public bool VipLifeAvailable { get; set; }

        /// <summary>Price of the next single life (one of the two is 0).</summary>
        public long NextLifeCoins { get; set; }

        public long NextLifeOrbes { get; set; }
    }

    public sealed class RewardDto
    {
        public long Coins { get; set; }

        public long Orbes { get; set; }

        public Dictionary<string, int> PowerUps { get; set; } = new Dictionary<string, int>();

        public List<string> Cosmetics { get; set; } = new List<string>();

        public int Lives { get; set; }

        public int BattlePassXp { get; set; }
    }

    public sealed class LoadoutEntryDto
    {
        /// <summary>PowerUpType name.</summary>
        public string Type { get; set; }

        public int Quantity { get; set; }
    }

    public sealed class VipDto
    {
        public int Tier { get; set; }

        public long LifetimeSpendCents { get; set; }

        public long? NextThresholdCents { get; set; }

        public float Progress { get; set; }
    }
}
