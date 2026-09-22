using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;

namespace CrushRoyale.Core.Economy
{
    /// <summary>A bundle of rewards (achievements, quests, battle pass, bosses...).</summary>
    public sealed class RewardData
    {
        public long Coins { get; set; }

        public long Orbes { get; set; }

        public Dictionary<PowerUpType, int> PowerUps { get; set; } = new Dictionary<PowerUpType, int>();

        public List<string> Cosmetics { get; set; } = new List<string>();

        public int Lives { get; set; }

        public int BattlePassXp { get; set; }

        /// <summary>Minutes of unlimited lives: the reward that removes the wall instead of paying it off once.</summary>
        public int UnlimitedLivesMinutes { get; set; }

        public bool IsEmpty => Coins == 0 && Orbes == 0 && PowerUps.Count == 0 && Cosmetics.Count == 0 && Lives == 0
            && BattlePassXp == 0 && UnlimitedLivesMinutes == 0;

        public static RewardData FromCurrency(long coins, long orbes = 0) => new RewardData { Coins = coins, Orbes = orbes };

        public RewardData AddPowerUp(PowerUpType type, int count)
        {
            if (count > 0)
            {
                PowerUps.TryGetValue(type, out int existing);
                PowerUps[type] = existing + count;
            }
            return this;
        }

        public RewardData Add(RewardData other)
        {
            if (other == null)
            {
                return this;
            }
            Coins += other.Coins;
            Orbes += other.Orbes;
            Lives += other.Lives;
            BattlePassXp += other.BattlePassXp;
            foreach (KeyValuePair<PowerUpType, int> kv in other.PowerUps)
            {
                AddPowerUp(kv.Key, kv.Value);
            }
            foreach (string c in other.Cosmetics)
            {
                if (!Cosmetics.Contains(c))
                {
                    Cosmetics.Add(c);
                }
            }
            return this;
        }

        /// <summary>Credits currencies and items. Lives/XP are applied by their own systems.</summary>
        public OperationResult GrantTo(IWallet wallet, Inventory inventory, TransactionReason reason, string reference, string idempotencyKey = null)
        {
            if (wallet == null)
            {
                throw new ArgumentNullException(nameof(wallet));
            }
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (Coins > 0)
            {
                OperationResult r = wallet.Credit(Currency.Coins, Coins, reason, reference, idempotencyKey == null ? null : idempotencyKey + ":c");
                if (!r.Success)
                {
                    return r;
                }
            }
            if (Orbes > 0)
            {
                OperationResult r = wallet.Credit(Currency.Orbes, Orbes, reason, reference, idempotencyKey == null ? null : idempotencyKey + ":o");
                if (!r.Success)
                {
                    return r;
                }
            }
            foreach (KeyValuePair<PowerUpType, int> kv in PowerUps)
            {
                inventory.Add(kv.Key, kv.Value);
            }
            foreach (string cosmetic in Cosmetics)
            {
                inventory.AddCosmetic(cosmetic);
            }
            return OperationResult.Ok();
        }
    }

    /// <summary>Serializable inventory.</summary>
    public sealed class InventoryState
    {
        public Dictionary<PowerUpType, int> PowerUps { get; set; } = new Dictionary<PowerUpType, int>();

        public HashSet<string> Cosmetics { get; set; } = new HashSet<string> { "board.classic", "pieces.classic", "emote.gg" };

        public string EquippedFrame { get; set; }

        public string EquippedBoardSkin { get; set; } = "board.classic";

        public string EquippedPieceSkin { get; set; } = "pieces.classic";

        public string EquippedTitle { get; set; }

        public string EquippedOutfit { get; set; }

        public bool AdsRemoved { get; set; }

        /// <summary>"Crown of Crystalheim" rare perk.</summary>
        public bool RarePerkUnlocked { get; set; }

        /// <summary>Battle pass season index with the premium track, or -1.</summary>
        public int PremiumPassSeason { get; set; } = -1;
    }

    public sealed class Inventory
    {
        public const int MaxStackPerPowerUp = 999;
        public const int MaxLoadoutQuantity = 3;

        public Inventory(InventoryState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        public InventoryState State { get; }

        public int Count(PowerUpType type) => State.PowerUps.TryGetValue(type, out int n) ? n : 0;

        public void Add(PowerUpType type, int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            State.PowerUps[type] = Math.Min(MaxStackPerPowerUp, Count(type) + count);
        }

        public OperationResult Consume(PowerUpType type, int count)
        {
            if (count <= 0)
            {
                return OperationResult.Fail(ErrorCode.InvalidArgument);
            }
            int have = Count(type);
            if (have < count)
            {
                return OperationResult.Fail(ErrorCode.NotEnoughItems, type + ": have " + have + ", need " + count);
            }
            State.PowerUps[type] = have - count;
            return OperationResult.Ok();
        }

        /// <summary>Server side, after a validated match: spends exactly what the replay used (all or nothing).</summary>
        public OperationResult ConsumeUsed(IReadOnlyDictionary<PowerUpType, int> used)
        {
            if (used == null)
            {
                return OperationResult.Ok();
            }
            foreach (KeyValuePair<PowerUpType, int> kv in used)
            {
                if (Count(kv.Key) < kv.Value)
                {
                    return OperationResult.Fail(ErrorCode.NotEnoughItems, "Replay used " + kv.Value + " " + kv.Key + " but inventory has " + Count(kv.Key));
                }
            }
            foreach (KeyValuePair<PowerUpType, int> kv in used)
            {
                if (kv.Value > 0)
                {
                    State.PowerUps[kv.Key] = Count(kv.Key) - kv.Value;
                }
            }
            return OperationResult.Ok();
        }

        /// <summary>Builds a pre-game loadout from selected types (once-per-match types bring 1, others up to 3).</summary>
        public OperationResult<List<LoadoutEntry>> BuildLoadout(IEnumerable<PowerUpType> selected, GameBalance balance, League highestLeague)
        {
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }

            var loadout = new List<LoadoutEntry>();
            var seen = new HashSet<PowerUpType>();
            foreach (PowerUpType type in selected ?? new PowerUpType[0])
            {
                if (!seen.Add(type))
                {
                    continue;
                }
                PowerUpDefinition def = balance.PowerUps.Get(type);
                if (highestLeague < def.UnlockLeague)
                {
                    return OperationResult<List<LoadoutEntry>>.Fail(ErrorCode.PowerUpLocked, type.ToString());
                }
                int count = Count(type);
                if (count <= 0)
                {
                    return OperationResult<List<LoadoutEntry>>.Fail(ErrorCode.NotEnoughItems, type.ToString());
                }
                loadout.Add(new LoadoutEntry(type, def.OncePerMatch ? 1 : Math.Min(MaxLoadoutQuantity, count)));
            }
            if (loadout.Count > balance.PowerUps.LoadoutSlots)
            {
                return OperationResult<List<LoadoutEntry>>.Fail(ErrorCode.LimitReached);
            }
            return OperationResult<List<LoadoutEntry>>.Ok(loadout);
        }

        public bool HasCosmetic(string id) => id != null && State.Cosmetics.Contains(id);

        public bool AddCosmetic(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Cosmetic id required.", nameof(id));
            }
            return State.Cosmetics.Add(id);
        }

        public OperationResult Equip(string cosmeticId)
        {
            CosmeticDefinition def = CosmeticCatalog.Get(cosmeticId);
            if (def == null)
            {
                return OperationResult.Fail(ErrorCode.NotFound);
            }
            if (!HasCosmetic(cosmeticId))
            {
                return OperationResult.Fail(ErrorCode.NotEnoughItems);
            }
            switch (def.Kind)
            {
                case CosmeticKind.AvatarFrame: State.EquippedFrame = cosmeticId; break;
                case CosmeticKind.BoardSkin: State.EquippedBoardSkin = cosmeticId; break;
                case CosmeticKind.PieceSkin: State.EquippedPieceSkin = cosmeticId; break;
                case CosmeticKind.Title: State.EquippedTitle = cosmeticId; break;
                case CosmeticKind.HeroOutfit: State.EquippedOutfit = cosmeticId; break;
                default: return OperationResult.Fail(ErrorCode.InvalidArgument, "Emotes are not equipped.");
            }
            return OperationResult.Ok();
        }
    }
}
