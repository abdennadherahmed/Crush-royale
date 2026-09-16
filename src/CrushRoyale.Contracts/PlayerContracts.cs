using System.Collections.Generic;

namespace CrushRoyale.Contracts
{
    public sealed class LoginRequest
    {
        /// <summary>SHA-256 of the device identifier, hashed on the device (never the raw id).</summary>
        public string DeviceHash { get; set; }

        public string Platform { get; set; }

        public string ClientVersion { get; set; }

        public int RulesVersion { get; set; }

        /// <summary>Hex of GameBalance.ComputeHash() the client runs with.</summary>
        public string BalanceHash { get; set; }

        public string Region { get; set; }

        public string Language { get; set; }
    }

    public sealed class LoginResponse
    {
        public ProfileDto Profile { get; set; }

        public bool IsNewPlayer { get; set; }

        /// <summary>True when the client must download /v1/config before playing.</summary>
        public bool ConfigOutdated { get; set; }

        public string BalanceHash { get; set; }

        public int RulesVersion { get; set; }

        public long ServerTimeUnixMs { get; set; }
    }

    public sealed class ConfigResponse
    {
        public int RulesVersion { get; set; }

        public string BalanceHash { get; set; }

        /// <summary>Serialized GameBalance (camelCase JSON).</summary>
        public string BalanceJson { get; set; }

        public long ServerTimeUnixMs { get; set; }
    }

    public sealed class HeroDto
    {
        public string Gender { get; set; }

        public string Name { get; set; }

        public int Appearance { get; set; }
    }

    public sealed class SetHeroRequest
    {
        public string Gender { get; set; }

        public string HeroName { get; set; }

        public int Appearance { get; set; }

        public string DisplayName { get; set; }

        /// <summary>Age gate answer (consumer protection for purchases). Optional.</summary>
        public int? Age { get; set; }

        public string Language { get; set; }
    }

    public sealed class PvpDto
    {
        public int Trophies { get; set; }

        public string League { get; set; }

        public string HighestLeague { get; set; }

        public int WinStreak { get; set; }

        public int BestWinStreak { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public int Draws { get; set; }

        public long BoostingCooldownUntilUnixMs { get; set; }
    }

    public sealed class StoryDto
    {
        public int HighestUnlockedStage { get; set; }

        public int TotalStars { get; set; }

        /// <summary>One char per stage from stage 1: '0' not won, '1'-'3' best stars.</summary>
        public string StarsByStage { get; set; }

        public List<string> Flags { get; set; } = new List<string>();

        public Dictionary<string, string> Choices { get; set; } = new Dictionary<string, string>();

        public string Ending { get; set; }

        public bool NewGamePlusUnlocked { get; set; }

        public List<string> Party { get; set; } = new List<string>();

        public List<string> UnlockedFeatures { get; set; } = new List<string>();

        public List<string> SeenEvents { get; set; } = new List<string>();
    }

    /// <summary>One pet of the collection, with what the UI needs to show progress and the awakening button.</summary>
    public sealed class PetDto
    {
        public string Type { get; set; }

        public bool Owned { get; set; }

        public int Level { get; set; }

        public int Xp { get; set; }

        /// <summary>Total XP of the next level (equals Xp at max level).</summary>
        public int XpForNext { get; set; }

        /// <summary>Total XP of the current level (progress bar start).</summary>
        public int XpForCurrent { get; set; }

        public int Fragments { get; set; }

        public bool CanAwaken { get; set; }

        public int AwakenFragments { get; set; }

        public int AwakenCoins { get; set; }

        /// <summary>Power-up offered once per match at max level.</summary>
        public string PowerUp { get; set; }

        /// <summary>Chance of a free automatic move per player move, in percent.</summary>
        public int AutoMovePercent { get; set; }
    }

    public sealed class PetsDto
    {
        public string Equipped { get; set; }

        public List<PetDto> Pets { get; set; } = new List<PetDto>();

        public int PullsSinceWholePet { get; set; }

        public int PityPulls { get; set; }

        public int WholePetOneIn { get; set; }

        public int SummonCostOrbes { get; set; }

        public int Summon10CostOrbes { get; set; }

        public int MaxLevel { get; set; }

        public int PvpLevelCap { get; set; }
    }

    public sealed class ChestSlotDto
    {
        public int Slot { get; set; }

        /// <summary>Wood, Silver, Gold, Crystal.</summary>
        public string Type { get; set; }

        /// <summary>Locked, Unlocking or Ready.</summary>
        public string Status { get; set; }

        public int SecondsLeft { get; set; }

        public int UnlockSeconds { get; set; }

        public int SkipCostOrbes { get; set; }
    }

    public sealed class ChestsDto
    {
        /// <summary>One entry per slot; null for an empty slot.</summary>
        public List<ChestSlotDto> Slots { get; set; } = new List<ChestSlotDto>();

        /// <summary>Server time used for the countdowns (the client adds the time elapsed since it received this).</summary>
        public long ServerNowUnixMs { get; set; }
    }

    public sealed class ChestOpenRequest
    {
        /// <summary>Open a chest that is not ready yet by paying its skip cost in orbes.</summary>
        public bool UseOrbes { get; set; }
    }

    public sealed class ChestOpenResponse
    {
        public string Type { get; set; }

        public RewardDto Reward { get; set; }

        public string FragmentsPet { get; set; }

        public int PetFragments { get; set; }

        public ChestsDto Chests { get; set; }

        public PetsDto Pets { get; set; }

        public WalletDto Wallet { get; set; }

        public InventoryDto Inventory { get; set; }
    }

    /// <summary>Batch of analytics events and client exceptions (sent every minute and when the app goes to background).</summary>
    public sealed class TelemetryRequest
    {
        public string SessionId { get; set; }

        public string AppVersion { get; set; }

        public string Device { get; set; }

        public string Os { get; set; }

        public List<TelemetryEventDto> Events { get; set; } = new List<TelemetryEventDto>();

        public List<ClientErrorDto> Errors { get; set; } = new List<ClientErrorDto>();
    }

    public sealed class TelemetryEventDto
    {
        public string Name { get; set; }

        public long AtUnixMs { get; set; }

        public Dictionary<string, string> Props { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ClientErrorDto
    {
        public string Message { get; set; }

        public string Stack { get; set; }

        public int Count { get; set; } = 1;
    }

    public sealed class PetSummonRequest
    {
        /// <summary>1 or 10.</summary>
        public int Count { get; set; }
    }

    public sealed class PetPullDto
    {
        public string Pet { get; set; }

        public bool WholePet { get; set; }

        public bool Duplicate { get; set; }

        public int Fragments { get; set; }
    }

    public sealed class PetSummonResponse
    {
        public List<PetPullDto> Pulls { get; set; } = new List<PetPullDto>();

        public PetsDto Pets { get; set; }

        public WalletDto Wallet { get; set; }
    }

    public sealed class PetRequest
    {
        public string Pet { get; set; }
    }

    public sealed class PetActionResponse
    {
        public PetsDto Pets { get; set; }

        public WalletDto Wallet { get; set; }
    }

    public sealed class InventoryDto
    {
        public Dictionary<string, int> PowerUps { get; set; } = new Dictionary<string, int>();

        public List<string> Cosmetics { get; set; } = new List<string>();

        public string EquippedFrame { get; set; }

        public string EquippedBoardSkin { get; set; }

        public string EquippedPieceSkin { get; set; }

        public string EquippedTitle { get; set; }

        public string EquippedOutfit { get; set; }

        public bool AdsRemoved { get; set; }

        public bool RarePerkUnlocked { get; set; }

        public bool PremiumPass { get; set; }
    }

    public sealed class ProfileDto
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public HeroDto Hero { get; set; }

        public bool HeroChosen { get; set; }

        public int? DeclaredAge { get; set; }

        public string Language { get; set; }

        public WalletDto Wallet { get; set; }

        public LivesDto Lives { get; set; }

        public VipDto Vip { get; set; }

        public PvpDto Pvp { get; set; }

        public StoryDto Story { get; set; }

        public InventoryDto Inventory { get; set; }

        public long? GuildId { get; set; }

        public bool LoginBonusAvailable { get; set; }

        public int LoginCalendarSlot { get; set; }

        public int CollectionPagesCompleted { get; set; }

        public int UnclaimedAchievements { get; set; }

        public int ClaimableQuests { get; set; }

        public PetsDto Pets { get; set; }

        public ChestsDto Chests { get; set; }

        public long SuspendedUntilUnixMs { get; set; }
    }

    public sealed class PublicProfileDto
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public int Trophies { get; set; }

        public string League { get; set; }

        public int HighestStage { get; set; }

        public long? GuildId { get; set; }

        public string Frame { get; set; }

        public string Title { get; set; }
    }

    public sealed class PlayerStatsDto
    {
        public PublicProfileDto Profile { get; set; }

        public int PvpWins { get; set; }

        public int PvpLosses { get; set; }

        public int BestWinStreak { get; set; }

        public string HighestLeague { get; set; }

        public int TotalStars { get; set; }

        public int AchievementsUnlocked { get; set; }

        public int? WeeklyRank { get; set; }
    }

    public sealed class SearchPlayersResponse
    {
        public List<PublicProfileDto> Players { get; set; } = new List<PublicProfileDto>();
    }

    // ------------------------------------------------------------------ progression

    public sealed class AchievementDto
    {
        public string Id { get; set; }

        public string Type { get; set; }

        public int Page { get; set; }

        public long Target { get; set; }

        public long Progress { get; set; }

        public bool Unlocked { get; set; }

        public bool Claimed { get; set; }

        public RewardDto Reward { get; set; }
    }

    public sealed class AchievementsResponse
    {
        public List<AchievementDto> Achievements { get; set; } = new List<AchievementDto>();

        public List<int> PagesCompleted { get; set; } = new List<int>();

        public float CoinBonus { get; set; }
    }

    public sealed class ClaimAchievementRequest
    {
        public string Id { get; set; }
    }

    public sealed class ClaimResponse
    {
        public RewardDto Reward { get; set; }

        public WalletDto Wallet { get; set; }

        public bool PageCompleted { get; set; }
    }

    public sealed class QuestDto
    {
        public string Id { get; set; }

        public string Type { get; set; }

        public int Target { get; set; }

        public int Progress { get; set; }

        public bool Claimed { get; set; }
    }

    public sealed class QuestsResponse
    {
        public List<QuestDto> Quests { get; set; } = new List<QuestDto>();

        public long ResetAtUnixMs { get; set; }
    }

    public sealed class LoginBonusResponse
    {
        public RewardDto Reward { get; set; }

        public int NextSlot { get; set; }

        public int TotalLoginDays { get; set; }

        public WalletDto Wallet { get; set; }
    }

    public sealed class BattlePassTierDto
    {
        public int Tier { get; set; }

        public RewardDto Free { get; set; }

        public RewardDto Premium { get; set; }

        public bool FreeClaimed { get; set; }

        public bool PremiumClaimed { get; set; }
    }

    public sealed class BattlePassResponse
    {
        public int Season { get; set; }

        public long Xp { get; set; }

        public int Tier { get; set; }

        public int XpPerTier { get; set; }

        public bool Premium { get; set; }

        public long SeasonEndUnixMs { get; set; }

        public List<BattlePassTierDto> Tiers { get; set; } = new List<BattlePassTierDto>();
    }

    public sealed class BattlePassClaimRequest
    {
        public int Tier { get; set; }

        public bool Premium { get; set; }
    }

    // ------------------------------------------------------------------ admin / reports

    public sealed class ReportCheatRequest
    {
        public string PlayerId { get; set; }

        public string MatchId { get; set; }

        public string Reason { get; set; }
    }

    public sealed class FlagDto
    {
        public long Id { get; set; }

        public string PlayerId { get; set; }

        public string Reason { get; set; }

        public string Severity { get; set; }

        public string Details { get; set; }

        public string MatchId { get; set; }

        public long CreatedAtUnixMs { get; set; }
    }

    public sealed class ReviewFlagRequest
    {
        public string Outcome { get; set; }

        public bool Punish { get; set; }

        public bool Severe { get; set; }
    }

    public sealed class AdminGrantRequest
    {
        public string PlayerId { get; set; }

        public long Coins { get; set; }

        public long Orbes { get; set; }

        public string Note { get; set; }
    }

    public sealed class UpdateBalanceRequest
    {
        public string BalanceJson { get; set; }
    }
}
