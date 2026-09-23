using System.Text;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Pets;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Social;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Story;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

/// <summary>Core/persistence objects to API contracts, plus input parsing helpers.</summary>
public static class Mappers
{
    public static WalletDto Wallet(PlayerWorkspace ws) => new() { Coins = ws.State.Wallet.Coins, Orbes = ws.State.Wallet.Orbes };

    /// <summary>The wheel as the hub needs it: the free spin, and the price of the next paid one.</summary>
    public static WheelStatusDto WheelStatus(PlayerWorkspace ws)
    {
        int today = TimeUtil.DayIndex(ws.Now);
        bool free = DailyWheel.CanSpin(ws.State.WheelDay, today);
        int extras = ws.State.WheelDay == today ? ws.State.WheelExtrasToday : 0;
        return new WheelStatusDto
        {
            FreeSpinAvailable = free,
            NextSpinOrbes = free ? 0 : DailyWheel.ExtraSpinCost(extras, ws.Balance.Economy.Wheel),
            ExtrasLeft = Math.Max(0, ws.Balance.Economy.Wheel.ExtraSpinsPerDay - extras)
        };
    }

    public static LivesDto Lives(PlayerWorkspace ws)
    {
        StaminaManager stamina = ws.Stamina;
        LifePurchaseQuote next = stamina.QuoteLives(1);
        return new LivesDto
        {
            Lives = stamina.Lives,
            MaxRegen = stamina.MaxRegenLives,
            RechargeSeconds = stamina.GetRechargeTimer(),
            FreeContinues = stamina.GetContinueCount(),
            VipLifeAvailable = stamina.CanClaimVipDailyLife(),
            NextLifeCoins = next.Coins,
            NextLifeOrbes = next.Orbes,
            UnlimitedSecondsLeft = stamina.UnlimitedSecondsLeft()
        };
    }

    public static VipDto Vip(PlayerWorkspace ws)
    {
        VipProgress progress = new VipSystem(ws.Balance).GetProgress(ws.State.Vip.LifetimeSpendCents);
        return new VipDto
        {
            Tier = (int)progress.Tier,
            LifetimeSpendCents = progress.LifetimeSpendCents,
            NextThresholdCents = progress.NextThresholdCents,
            Progress = progress.ProgressToNext
        };
    }

    /// <summary>
    /// The jar, as the client needs to see it: what is inside, what it holds, and whether it is worth offering yet.
    /// </summary>
    public static PiggyBankDto PiggyBank(PlayerWorkspace ws)
    {
        CrushRoyale.Core.Economy.PiggyBankBalance b = ws.Balance.Economy.PiggyBank;
        long orbes = ws.State.PiggyBank?.Orbes ?? 0;
        return new PiggyBankDto
        {
            Orbes = orbes,
            Cap = b.CapOrbes,
            TimesBroken = ws.State.PiggyBank?.TimesBroken ?? 0,
            Offered = orbes >= b.MinOrbesToOffer,
            Sku = b.Sku,
            PriceCents = b.PriceCents
        };
    }

    public static PvpDto Pvp(PlayerWorkspace ws)
    {
        PlayerTrophyRecord r = ws.State.Pvp;
        return new PvpDto
        {
            Trophies = r.Trophies,
            League = LeagueTable.GetLeague(r.Trophies, ws.Balance.Trophies).ToString(),
            HighestLeague = r.HighestLeague.ToString(),
            WinStreak = r.CurrentWinStreak,
            BestWinStreak = r.BestWinStreak,
            Wins = r.Wins,
            Losses = r.Losses,
            Draws = r.Draws,
            BoostingCooldownUntilUnixMs = ws.State.Integrity.BoostingCooldownUntilUnixMs
        };
    }

    public static RestorationDto Restoration(StoryProgress p)
    {
        RestorationState state = p.Restoration ?? new RestorationState();
        return new RestorationDto
        {
            StarsAvailable = Core.Story.Restoration.AvailableStars(p),
            StarsSpent = state.StarsSpent,
            Built = state.Built.OrderBy(b => b, StringComparer.Ordinal).ToList(),
            CurrentZone = Core.Story.Restoration.CurrentZone(state)?.Number ?? 0,
            CanBuild = Core.Story.Restoration.CanBuildSomething(p)
        };
    }

    public static StoryDto Story(PlayerWorkspace ws)
    {
        StoryProgress p = ws.State.Story;
        int maxStage = Math.Max(p.HighestUnlockedStage - 1, p.Stages.Count == 0 ? 0 : p.Stages.Keys.Max());
        var stars = new StringBuilder(maxStage);
        for (int id = 1; id <= maxStage; id++)
        {
            stars.Append(p.Stages.TryGetValue(id, out StageProgress? s) && s.EverWon ? (char)('0' + Math.Clamp(s.BestStars, 1, 3)) : '0');
        }

        return new StoryDto
        {
            HighestUnlockedStage = p.HighestUnlockedStage,
            TotalStars = p.TotalStars,
            StarsByStage = stars.ToString(),
            Flags = p.Flags.OrderBy(f => f, StringComparer.Ordinal).ToList(),
            Choices = new Dictionary<string, string>(p.Choices),
            Ending = p.Ending?.ToString(),
            NewGamePlusUnlocked = p.NewGamePlusUnlocked,
            Party = ws.Story.GetParty().Select(c => c.Id).ToList(),
            UnlockedFeatures = Enum.GetValues<Feature>().Where(ws.Story.IsFeatureUnlocked).Select(f => f.ToString()).ToList(),
            SeenEvents = p.SeenEvents.OrderBy(e => e, StringComparer.Ordinal).ToList(),
            ClaimedChapterChests = (p.ClaimedChapterChests ?? new HashSet<string>()).OrderBy(c => c, StringComparer.Ordinal).ToList(),
            WinStreak = p.WinStreak,
            BestWinStreak = p.BestWinStreak,
            Restoration = Restoration(p)
        };
    }

    public static InventoryDto Inventory(PlayerWorkspace ws)
    {
        InventoryState s = ws.State.Inventory;
        return new InventoryDto
        {
            PowerUps = s.PowerUps.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            Cosmetics = s.Cosmetics.OrderBy(c => c, StringComparer.Ordinal).ToList(),
            EquippedFrame = s.EquippedFrame,
            EquippedBoardSkin = s.EquippedBoardSkin,
            EquippedPieceSkin = s.EquippedPieceSkin,
            EquippedTitle = s.EquippedTitle,
            EquippedOutfit = s.EquippedOutfit,
            AdsRemoved = s.AdsRemoved,
            RarePerkUnlocked = s.RarePerkUnlocked,
            PremiumPass = s.PremiumPassSeason == ws.BattlePassSeason
        };
    }

    public static ProfileDto Profile(PlayerWorkspace ws)
    {
        PlayerState s = ws.State;
        return new ProfileDto
        {
            Id = ws.IdString,
            DisplayName = s.DisplayName,
            PiggyBank = PiggyBank(ws),
            Hero = new HeroDto { Gender = s.Hero.Gender, Name = s.Hero.Name, Appearance = s.Hero.Appearance },
            HeroChosen = s.HeroChosen,
            DeclaredAge = s.DeclaredAge,
            Language = s.Language,
            Wallet = Wallet(ws),
            Lives = Lives(ws),
            Vip = Vip(ws),
            Pvp = Pvp(ws),
            Story = Story(ws),
            Inventory = Inventory(ws),
            GuildId = s.GuildId,
            LoginBonusAvailable = LoginCalendar.CanClaim(s.Login, ws.Now),
            VipGiftAvailable = VipGifts.CanClaim((int)s.Vip.Tier, s.VipGiftDay, TimeUtil.DayIndex(ws.Now)),
            WheelAvailable = DailyWheel.CanSpin(s.WheelDay, TimeUtil.DayIndex(ws.Now)),
            Wheel = WheelStatus(ws),
            LoginCalendarSlot = s.Login.NextSlot,
            CollectionPagesCompleted = s.Achievements.PagesCompleted.Count,
            UnclaimedAchievements = ws.Achievements.GetUnclaimed().Count,
            ClaimableQuests = s.Quests.Day == TimeUtil.DayIndex(ws.Now) ? s.Quests.Quests.Count(q => q.IsComplete && !q.Claimed) : 0,
            Pets = Pets(ws),
            Chests = Chests(ws),
            SuspendedUntilUnixMs = s.Integrity.PermanentlyBanned ? long.MaxValue : s.Integrity.SuspendedUntilUnixMs
        };
    }

    public static ChestsDto Chests(PlayerWorkspace ws)
    {
        ChestSystem chests = ws.Chests;
        long now = ws.NowMs;
        var dto = new ChestsDto
        {
            ServerNowUnixMs = now,
            FreeChestSecondsLeft = chests.FreeChestSecondsLeft(now),
            WinsToGold = chests.WinsToGold(),
            WinsToCrystal = chests.WinsToCrystal()
        };
        for (int i = 0; i < chests.State.Slots.Count; i++)
        {
            ChestSlot slot = chests.State.Slots[i];
            dto.Slots.Add(slot == null ? null! : new ChestSlotDto
            {
                Slot = i,
                Type = slot.Type.ToString(),
                Status = chests.Status(i, now).ToString(),
                SecondsLeft = chests.SecondsLeft(i, now),
                UnlockSeconds = slot.UnlockSeconds,
                SkipCostOrbes = chests.SkipCostOrbes(i, now)
            });
        }
        return dto;
    }

    public static PetsDto Pets(PlayerWorkspace ws)
    {
        PetCollection pets = ws.Pets;
        PetBalance balance = ws.Balance.Pets;
        var dto = new PetsDto
        {
            Equipped = pets.State.Equipped == PetType.None ? null : pets.State.Equipped.ToString(),
            PullsSinceWholePet = pets.State.PullsSinceWholePet,
            PityPulls = balance.PityPulls,
            UnlockFragments = balance.UnlockFragments,
            WholePetOneIn = balance.WholePetOneIn,
            SummonCostOrbes = balance.SummonCostOrbes,
            Summon10CostOrbes = balance.Summon10CostOrbes,
            MaxLevel = balance.MaxLevel,
            PvpLevelCap = balance.PvpLevelCap
        };
        foreach (PetDefinition def in balance.Pets)
        {
            PetState state = pets.State.Pets.TryGetValue(def.Type, out PetState s) ? s : new PetState();
            int level = Math.Max(state.Owned ? 1 : 0, state.Level);
            bool canAwaken = pets.CanAwaken(def.Type, out int fragments, out int coins);
            dto.Pets.Add(new PetDto
            {
                Type = def.Type.ToString(),
                Owned = state.Owned,
                Level = level,
                Xp = state.Xp,
                XpForCurrent = level >= 1 ? balance.XpForLevel[Math.Min(level, balance.MaxLevel) - 1] : 0,
                XpForNext = level >= 1 && level < balance.MaxLevel ? balance.XpForLevel[level] : state.Xp,
                Fragments = state.Fragments,
                CanAwaken = canAwaken,
                AwakenFragments = fragments,
                AwakenCoins = coins,
                PowerUp = def.PowerUp.ToString(),
                AutoMovePercent = level * balance.AutoMovePermillePerLevel / 10
            });
        }
        return dto;
    }

    /// <summary>Duel entry as the owner of the entry sees it.</summary>
    public static FriendDuelDto Duel(FriendDuel duel) => new FriendDuelDto
    {
        Id = duel.Id,
        OpponentId = duel.OpponentId,
        OpponentName = duel.OpponentName,
        State = duel.State.ToString(),
        Outcome = duel.Outcome.ToString(),
        MyScore = duel.MyScore,
        OpponentScore = duel.OpponentScore,
        IamChallenger = duel.IamChallenger
    };

    public static PublicProfileDto Public(PlayerSummary p) => new()
    {
        Id = p.Id.ToString(),
        DisplayName = p.DisplayName,
        Trophies = p.Trophies,
        League = p.League.ToString(),
        HighestStage = p.HighestStage,
        GuildId = p.GuildId,
        Frame = p.Frame,
        Title = p.Title
    };

    public static RewardDto Reward(RewardData? reward) => reward == null ? new RewardDto() : new RewardDto
    {
        Coins = reward.Coins,
        Orbes = reward.Orbes,
        PowerUps = reward.PowerUps.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        Cosmetics = reward.Cosmetics.ToList(),
        Lives = reward.Lives,
        BattlePassXp = reward.BattlePassXp
    };

    public static List<LoadoutEntryDto> Loadout(IEnumerable<LoadoutEntry> loadout) =>
        loadout.Select(l => new LoadoutEntryDto { Type = l.Type.ToString(), Quantity = l.Quantity }).ToList();

    public static StoryEventDto Event(StoryEvent e) => new()
    {
        Id = e.Id,
        StageId = e.StageId,
        Trigger = e.Trigger.ToString(),
        DialogueId = e.DialogueId,
        Cinematic = e.Cinematic,
        CharacterJoins = e.CharacterJoins,
        CharacterLeaves = e.CharacterLeaves,
        ChoiceId = e.ChoiceId
    };

    public static ShopItemDto ShopItem(ShopItem i) => new()
    {
        Id = i.Id,
        Kind = i.Kind.ToString(),
        PowerUp = i.Kind is ShopItemKind.PowerUp or ShopItemKind.PowerUpBundle ? i.PowerUp.ToString() : null,
        Quantity = i.Quantity,
        PriceCoins = i.PriceCoins,
        PriceOrbes = i.PriceOrbes,
        PriceCents = i.PriceCents,
        Sku = i.Sku,
        IsDeal = i.IsDeal,
        DiscountPermille = i.DiscountPermille,
        RequiredVip = i.RequiredVip,
        CosmeticId = i.CosmeticId,
        OrbesGranted = i.OrbesGranted,
        CoinsGranted = i.CoinsGranted,
        OrbesPerEuro = i.OrbesPerEuro,
        SoldOut = i.SoldOut
    };

    public static MatchStartResponse MatchStart(MatchRow match, PlayerWorkspace ws, string balanceHash, GhostDto? ghost = null) => new()
    {
        MatchId = match.Id,
        Mode = match.Mode.ToString(),
        Seed = match.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
        StageId = match.StageId,
        Loadout = Loadout(match.Config.Loadout),
        HighestLeague = match.Config.HighestLeague.ToString(),
        AssistExtraMoves = match.Config.AssistExtraMoves,
        StartBoosters = match.Config.StartBoosters,
        Pet = match.Config.Pet == PetType.None ? null : match.Config.Pet.ToString(),
        PetLevel = match.Config.PetLevel,
        StartedAtUnixMs = TimeUtil.ToUnixMs(match.StartedAt),
        BalanceHash = balanceHash,
        Lives = Lives(ws),
        Ghost = ghost
    };

    // ------------------------------------------------------------------ input parsing

    public static T ParseEnum<T>(string? value, string field) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse(value.Trim(), ignoreCase: true, out T parsed) || !Enum.IsDefined(parsed) || int.TryParse(value, out _))
        {
            throw new ApiException(ErrorCode.InvalidArgument, "Invalid " + field + ": '" + value + "'.");
        }
        return parsed;
    }

    public static List<PowerUpType> ParseLoadout(IEnumerable<string>? names, GameBalance balance)
    {
        var list = (names ?? Enumerable.Empty<string>()).Select(n => ParseEnum<PowerUpType>(n, "power-up")).Distinct().ToList();
        if (list.Count > balance.PowerUps.LoadoutSlots)
        {
            throw new ApiException(ErrorCode.LimitReached, "At most " + balance.PowerUps.LoadoutSlots + " power-ups per match.");
        }
        return list;
    }

    public static ReplayData DecodeReplay(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64) || base64.Length > 90000)
        {
            throw new ApiException(ErrorCode.ReplayInvalid, "Replay missing or too large.");
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new ApiException(ErrorCode.ReplayInvalid, "Replay is not valid base64.");
        }
        return ReplaySerializer.TryDeserialize(bytes).ValueOrThrow();
    }

    public static string NewId(string prefix) => prefix + "_" + Guid.NewGuid().ToString("N");
}
