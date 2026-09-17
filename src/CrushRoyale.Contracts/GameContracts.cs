using System.Collections.Generic;

namespace CrushRoyale.Contracts
{
    // ------------------------------------------------------------------ matches (story, pvp, guild boss)

    public sealed class StartStageRequest
    {
        /// <summary>PowerUpType names selected before the match (max 3).</summary>
        public List<string> Loadout { get; set; } = new List<string>();
    }

    public sealed class GhostDto
    {
        public string ReplayBase64 { get; set; }

        public string OpponentId { get; set; }

        public string OpponentName { get; set; }

        public int OpponentTrophies { get; set; }

        public string OpponentLeague { get; set; }

        public string OpponentFrame { get; set; }

        public string OpponentTitle { get; set; }

        public List<LoadoutEntryDto> OpponentLoadout { get; set; } = new List<LoadoutEntryDto>();
    }

    /// <summary>Everything the client needs to build the exact SessionConfig the server will validate against.</summary>
    public sealed class MatchStartResponse
    {
        public string MatchId { get; set; }

        /// <summary>GameMode name.</summary>
        public string Mode { get; set; }

        /// <summary>Unsigned 64-bit seed as a decimal string (JSON number precision).</summary>
        public string Seed { get; set; }

        public int StageId { get; set; }

        public List<LoadoutEntryDto> Loadout { get; set; } = new List<LoadoutEntryDto>();

        public string HighestLeague { get; set; }

        public int AssistExtraMoves { get; set; }

        /// <summary>Equipped pet and the level it plays at in this match (capped in PvP).</summary>
        public string Pet { get; set; }

        public int PetLevel { get; set; }

        public long StartedAtUnixMs { get; set; }

        public string BalanceHash { get; set; }

        public LivesDto Lives { get; set; }

        /// <summary>Opponent ghost (PvP) when already available.</summary>
        public GhostDto Ghost { get; set; }

        /// <summary>Guild boss attacks: the boss HP when the attack starts (0 otherwise).</summary>
        public long BossMaxHp { get; set; }

        public long BossRemainingHp { get; set; }
    }

    public sealed class ContinueRequest
    {
        public bool UseFreeContinue { get; set; }
    }

    public sealed class ContinueResponse
    {
        public int ContinuesAuthorized { get; set; }

        public int OrbesSpent { get; set; }

        public bool Free { get; set; }

        public WalletDto Wallet { get; set; }
    }

    public sealed class SubmitReplayRequest
    {
        public string MatchId { get; set; }

        /// <summary>ReplaySerializer bytes, base64.</summary>
        public string ReplayBase64 { get; set; }
    }

    public sealed class StoryEventDto
    {
        public string Id { get; set; }

        public int StageId { get; set; }

        public string Trigger { get; set; }

        public string DialogueId { get; set; }

        public bool Cinematic { get; set; }

        public string CharacterJoins { get; set; }

        public string CharacterLeaves { get; set; }

        public string ChoiceId { get; set; }
    }

    public sealed class StageCompleteResponse
    {
        public bool Accepted { get; set; }

        /// <summary>ErrorCode name when not accepted (ReplayMismatch, VersionMismatch...).</summary>
        public string Error { get; set; }

        public string State { get; set; }

        public long Score { get; set; }

        public int Stars { get; set; }

        public bool FirstWin { get; set; }

        public long CoinsEarned { get; set; }

        public long OrbesEarned { get; set; }

        public int NextStage { get; set; }

        public List<string> FeaturesUnlocked { get; set; } = new List<string>();

        public List<StoryEventDto> Events { get; set; } = new List<StoryEventDto>();

        public List<string> CharactersJoined { get; set; } = new List<string>();

        public List<string> AchievementsUnlocked { get; set; } = new List<string>();

        public bool ShowInterstitial { get; set; }

        /// <summary>Chest granted by this stage (welcome chest, chapter bosses), null otherwise.</summary>
        public string ChestEarned { get; set; }

        public WalletDto Wallet { get; set; }

        public LivesDto Lives { get; set; }
    }

    public sealed class ChoiceRequest
    {
        public string ChoiceId { get; set; }

        public string OptionId { get; set; }
    }

    public sealed class ChoiceResponse
    {
        public string Ending { get; set; }

        public List<string> CosmeticsGranted { get; set; } = new List<string>();

        public List<string> AchievementsUnlocked { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------ economy

    public sealed class BuyLivesRequest
    {
        public int Count { get; set; }
    }

    public sealed class ShopItemDto
    {
        public string Id { get; set; }

        public string Kind { get; set; }

        public string PowerUp { get; set; }

        public int Quantity { get; set; }

        public int PriceCoins { get; set; }

        public int PriceOrbes { get; set; }

        public int PriceCents { get; set; }

        public string Sku { get; set; }

        public bool IsDeal { get; set; }

        public int DiscountPermille { get; set; }

        public int RequiredVip { get; set; }

        public string CosmeticId { get; set; }

        public int OrbesGranted { get; set; }

        public int CoinsGranted { get; set; }

        public decimal OrbesPerEuro { get; set; }

        public bool SoldOut { get; set; }
    }

    public sealed class ShopResponse
    {
        public List<ShopItemDto> Items { get; set; } = new List<ShopItemDto>();

        public long NextRefreshUnixMs { get; set; }

        public int RefreshCostOrbes { get; set; }

        public WalletDto Wallet { get; set; }
    }

    public sealed class PurchaseRequest
    {
        public string ItemId { get; set; }

        /// <summary>PaymentMethod name: Coins or Orbes.</summary>
        public string Method { get; set; }
    }

    public sealed class PurchaseResponse
    {
        public RewardDto Granted { get; set; }

        public WalletDto Wallet { get; set; }

        public LivesDto Lives { get; set; }
    }

    public sealed class IapPrecheckRequest
    {
        public string Sku { get; set; }
    }

    public sealed class IapPrecheckResponse
    {
        public bool Allowed { get; set; }

        public string Error { get; set; }

        public int PriceCents { get; set; }
    }

    public sealed class IapValidateRequest
    {
        public string Sku { get; set; }

        public string PurchaseToken { get; set; }

        public string OrderId { get; set; }
    }

    public sealed class IapValidateResponse
    {
        public bool AlreadyGranted { get; set; }

        public RewardDto Reward { get; set; }

        public WalletDto Wallet { get; set; }

        public VipDto Vip { get; set; }

        public InventoryDto Inventory { get; set; }
    }

    public sealed class EquipRequest
    {
        public string CosmeticId { get; set; }
    }

    public sealed class RewardedAdRequest
    {
        /// <summary>"life" or "coins".</summary>
        public string Placement { get; set; }

        /// <summary>Ad network server-side verification token when available.</summary>
        public string AdToken { get; set; }
    }

    public sealed class RewardedAdResponse
    {
        public RewardDto Reward { get; set; }

        public int RemainingToday { get; set; }

        public WalletDto Wallet { get; set; }

        public LivesDto Lives { get; set; }
    }

    // ------------------------------------------------------------------ pvp

    public sealed class MatchmakingRequest
    {
        public List<string> Loadout { get; set; } = new List<string>();
    }

    public sealed class MatchmakingStatusResponse
    {
        /// <summary>Waiting, Matched, TimedOut, Cancelled, None.</summary>
        public string Status { get; set; }

        public int WaitedMs { get; set; }

        public int Range { get; set; }

        public MatchStartResponse Match { get; set; }
    }

    /// <summary>A shareable "challenge me" code for the player's latest duel.</summary>
    public sealed class ChallengeCreateResponse
    {
        public string Code { get; set; }

        public string Url { get; set; }

        public long Score { get; set; }

        public string DisplayName { get; set; }
    }

    public sealed class PvpResultDto
    {
        public bool Accepted { get; set; }

        public string Error { get; set; }

        /// <summary>Win, Loss, Draw, or Pending (live opponent still playing).</summary>
        public string Outcome { get; set; }

        public long Score { get; set; }

        public long OpponentScore { get; set; }

        public long FrozenPoints { get; set; }

        public long OpponentFrozenPoints { get; set; }

        public int TrophyDelta { get; set; }

        public int Trophies { get; set; }

        public string League { get; set; }

        public bool Promoted { get; set; }

        public bool Demoted { get; set; }

        public int FirstReachOrbes { get; set; }

        public long CoinsEarned { get; set; }

        /// <summary>Chest won with this victory (null when none, "Full" when every slot was taken).</summary>
        public string ChestEarned { get; set; }

        public bool Ranked { get; set; }

        public List<string> AchievementsUnlocked { get; set; } = new List<string>();

        public bool ShowInterstitial { get; set; }

        public WalletDto Wallet { get; set; }
    }

    public sealed class MatchInfoResponse
    {
        public string MatchId { get; set; }

        public string Mode { get; set; }

        public string Status { get; set; }

        public string Seed { get; set; }

        public GhostDto Ghost { get; set; }

        public PvpResultDto Result { get; set; }
    }

    public sealed class LeaderboardEntryDto
    {
        public int Rank { get; set; }

        public string PlayerId { get; set; }

        public string DisplayName { get; set; }

        public int Trophies { get; set; }

        public string League { get; set; }
    }

    public sealed class LeaderboardResponse
    {
        public string League { get; set; }

        public int Week { get; set; }

        public long ResetAtUnixMs { get; set; }

        public List<LeaderboardEntryDto> Entries { get; set; } = new List<LeaderboardEntryDto>();

        public int? MyRank { get; set; }
    }

    public sealed class GuildRankDto
    {
        public int Rank { get; set; }

        public long GuildId { get; set; }

        public string Name { get; set; }

        public long TotalTrophies { get; set; }

        public int Members { get; set; }

        public int Level { get; set; }
    }

    public sealed class GuildLeaderboardResponse
    {
        public List<GuildRankDto> Entries { get; set; } = new List<GuildRankDto>();

        public int? MyGuildRank { get; set; }

        public long ResetAtUnixMs { get; set; }
    }

    // ------------------------------------------------------------------ friends

    public sealed class FriendTargetRequest
    {
        public string PlayerId { get; set; }
    }

    public sealed class FriendsResponse
    {
        public List<PublicProfileDto> Friends { get; set; } = new List<PublicProfileDto>();

        public List<PublicProfileDto> Incoming { get; set; } = new List<PublicProfileDto>();

        public List<PublicProfileDto> Outgoing { get; set; } = new List<PublicProfileDto>();

        public Dictionary<string, long> ChallengeCooldownMs { get; set; } = new Dictionary<string, long>();
    }

    public sealed class WorldMapFriendDto
    {
        public string PlayerId { get; set; }

        public string DisplayName { get; set; }

        public int Stage { get; set; }

        public string Frame { get; set; }
    }

    public sealed class WorldMapResponse
    {
        public int MyStage { get; set; }

        public List<WorldMapFriendDto> Friends { get; set; } = new List<WorldMapFriendDto>();
    }

    // ------------------------------------------------------------------ guilds

    public sealed class CreateGuildRequest
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public bool IsOpen { get; set; } = true;

        public int MinTrophies { get; set; }
    }

    public sealed class GuildSettingsRequest
    {
        public string Description { get; set; }

        public bool IsOpen { get; set; }

        public int MinTrophies { get; set; }
    }

    public sealed class GuildMemberDto
    {
        public string PlayerId { get; set; }

        public string DisplayName { get; set; }

        public string Role { get; set; }

        public int Trophies { get; set; }

        public long DonatedCoins { get; set; }

        public long DonatedOrbes { get; set; }

        public int BossAttacksLeft { get; set; }

        public long BossDamage { get; set; }

        /// <summary>Coins donated today (the coin allowance resets daily).</summary>
        public long CoinsDonatedToday { get; set; }

        public long JoinedAtUnixMs { get; set; }
    }

    public sealed class GuildBossDto
    {
        public int Week { get; set; }

        public int BossIndex { get; set; }

        public long MaxHp { get; set; }

        public long Damage { get; set; }

        public bool Defeated { get; set; }

        public long ResetAtUnixMs { get; set; }
    }

    public sealed class GuildDto
    {
        public long Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public int Level { get; set; }

        public long DonationProgress { get; set; }

        public string NextLevelCurrency { get; set; }

        public long NextLevelCost { get; set; }

        /// <summary>Coins per guild point (one orbe = one point).</summary>
        public int CoinsPerPoint { get; set; }

        /// <summary>Daily coin donation allowance per member.</summary>
        public long DailyCoinCap { get; set; }

        public int TechPoints { get; set; }

        public Dictionary<string, int> Tech { get; set; } = new Dictionary<string, int>();

        public List<GuildMemberDto> Members { get; set; } = new List<GuildMemberDto>();

        public bool IsOpen { get; set; }

        public int MinTrophies { get; set; }

        public long TotalTrophies { get; set; }

        public GuildBossDto Boss { get; set; }

        public int Rank { get; set; }
    }

    public sealed class GuildSummaryDto
    {
        public long Id { get; set; }

        public string Name { get; set; }

        public int Level { get; set; }

        public int Members { get; set; }

        public long TotalTrophies { get; set; }

        public bool IsOpen { get; set; }

        public int MinTrophies { get; set; }
    }

    public sealed class GuildSearchResponse
    {
        public List<GuildSummaryDto> Guilds { get; set; } = new List<GuildSummaryDto>();
    }

    public sealed class GuildMemberActionRequest
    {
        public string PlayerId { get; set; }

        /// <summary>GuildRole name for role changes.</summary>
        public string Role { get; set; }
    }

    public sealed class DonateRequest
    {
        public long Amount { get; set; }

        /// <summary>"Coins" (daily allowance) or "Orbes" (unlimited). Empty means orbes.</summary>
        public string Currency { get; set; }
    }

    public sealed class DonateResponse
    {
        public long Paid { get; set; }

        public string Currency { get; set; }

        public bool LeveledUp { get; set; }

        /// <summary>Levels gained by this donation: a big gift fills several levels at once.</summary>
        public int LevelsGained { get; set; }

        public long Points { get; set; }

        public GuildDto Guild { get; set; }

        public WalletDto Wallet { get; set; }
    }

    public sealed class TechRequest
    {
        public string Tech { get; set; }
    }

    public sealed class GuildBossAttackResponse
    {
        public bool Accepted { get; set; }

        public string Error { get; set; }

        public long Score { get; set; }

        public long Damage { get; set; }

        public int AttacksLeft { get; set; }

        public bool DefeatedNow { get; set; }

        /// <summary>Other members defeated the boss while this attack was played: damage counted, attack kept, reward shared.</summary>
        public bool DefeatedDuringAttack { get; set; }

        public RewardDto Reward { get; set; }

        public GuildBossDto Boss { get; set; }

        public List<string> AchievementsUnlocked { get; set; } = new List<string>();
    }

    public sealed class ChatSendRequest
    {
        public string Text { get; set; }
    }

    public sealed class ChatMessageDto
    {
        public long Id { get; set; }

        public string PlayerId { get; set; }

        public string DisplayName { get; set; }

        public string Body { get; set; }

        public bool Masked { get; set; }

        public long CreatedAtUnixMs { get; set; }
    }

    public sealed class ChatHistoryResponse
    {
        public List<ChatMessageDto> Messages { get; set; } = new List<ChatMessageDto>();
    }
}
