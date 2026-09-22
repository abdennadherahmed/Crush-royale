using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;

namespace CrushRoyale.Core.Progression
{
    /// <summary>Victory chest rarity. Persisted: never reorder.</summary>
    public enum ChestType : byte
    {
        Wood = 0,
        Silver = 1,
        Gold = 2,
        Crystal = 3
    }

    /// <summary>Unlock time and content of one chest rarity.</summary>
    public sealed class ChestDefinition
    {
        public ChestType Type { get; set; }

        public int UnlockSeconds { get; set; }

        public int DropWeight { get; set; }

        public int CoinsMin { get; set; }

        public int CoinsMax { get; set; }

        public int PowerUps { get; set; }

        public int PetFragmentsMin { get; set; }

        public int PetFragmentsMax { get; set; }

        /// <summary>Chance (permille) to also contain orbes.</summary>
        public int OrbesChancePermille { get; set; }

        public int Orbes { get; set; }
    }

    /// <summary>Victory chests (Clash Royale style): won in PvP and on story milestones, opened after a timer or with orbes.</summary>
    public sealed class ChestBalance
    {
        public int Slots { get; set; } = 4;

        /// <summary>
        /// A gold chest is guaranteed at the latest on this many victory chests, and a crystal one on this many.
        /// Zero turns the guarantee off and leaves the raw weights, which is a worse game: the players who quit are
        /// the ones who hit a long streak of nothing, not the ones who get an average run.
        /// </summary>
        public int GoldAtLatest { get; set; } = 10;

        public int CrystalAtLatest { get; set; } = 40;

        /// <summary>
        /// A chest that fills by itself every few hours and asks nothing in return. It is the appointment for the
        /// player who has run out of lives or lost three in a row: a reason to open the game that is not "play more".
        /// </summary>
        public int FreeChestSeconds { get; set; } = 4 * 3600;

        public ChestType FreeChestType { get; set; } = ChestType.Wood;

        /// <summary>Orbes to open a chest immediately: 1 per started 10 minutes left.</summary>
        public int SkipSecondsPerOrbe { get; set; } = 600;

        /// <summary>The first chest (after the 3rd story stage) opens in a few minutes so new players see the loop at once.</summary>
        public int WelcomeChestUnlockSeconds { get; set; } = 180;

        public int WelcomeChestStage { get; set; } = 3;

        public List<ChestDefinition> Chests { get; set; } = new List<ChestDefinition>
        {
            new ChestDefinition { Type = ChestType.Wood, UnlockSeconds = 3 * 3600, DropWeight = 60, CoinsMin = 40, CoinsMax = 80, PowerUps = 1, PetFragmentsMin = 2, PetFragmentsMax = 4 },
            new ChestDefinition { Type = ChestType.Silver, UnlockSeconds = 8 * 3600, DropWeight = 28, CoinsMin = 100, CoinsMax = 180, PowerUps = 2, PetFragmentsMin = 5, PetFragmentsMax = 10, OrbesChancePermille = 30, Orbes = 3 },
            new ChestDefinition { Type = ChestType.Gold, UnlockSeconds = 12 * 3600, DropWeight = 10, CoinsMin = 250, CoinsMax = 400, PowerUps = 3, PetFragmentsMin = 12, PetFragmentsMax = 20, OrbesChancePermille = 120, Orbes = 6 },
            new ChestDefinition { Type = ChestType.Crystal, UnlockSeconds = 24 * 3600, DropWeight = 2, CoinsMin = 600, CoinsMax = 900, PowerUps = 5, PetFragmentsMin = 30, PetFragmentsMax = 50, OrbesChancePermille = 1000, Orbes = 12 }
        };

        public ChestDefinition Get(ChestType type) => Chests.Find(c => c.Type == type) ?? Chests[0];
    }

    public sealed class ChestSlot
    {
        public ChestType Type { get; set; }

        public int UnlockSeconds { get; set; }

        /// <summary>0 while locked; set when the player starts the timer.</summary>
        public long UnlockStartedUnixMs { get; set; }

        public string Source { get; set; } = string.Empty;
    }

    public sealed class ChestState
    {
        /// <summary>Fixed-size slot list; null entries are empty slots.</summary>
        public List<ChestSlot> Slots { get; set; } = new List<ChestSlot>();

        public long ChestsOpened { get; set; }

        /// <summary>Victory chests rolled since the last gold one, and since the last crystal one (see the cycle).</summary>
        public int SinceGold { get; set; }

        public int SinceCrystal { get; set; }

        /// <summary>When the free chest was last taken; it comes back on its own clock, whether or not you play.</summary>
        public long FreeTakenUnixMs { get; set; }
    }

    public enum ChestSlotStatus
    {
        Empty,
        Locked,
        Unlocking,
        Ready
    }

    /// <summary>What an opened chest gave.</summary>
    public sealed class ChestContent
    {
        public ChestType Type { get; set; }

        public RewardData Reward { get; set; } = new RewardData();

        public PetType FragmentsPet { get; set; }

        public int PetFragments { get; set; }

        public int OrbesSpent { get; set; }
    }

    public sealed class ChestSystem
    {
        private readonly ChestBalance _balance;
        private readonly GameBalance _game;

        public ChestSystem(ChestState state, GameBalance balance)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            _game = balance ?? throw new ArgumentNullException(nameof(balance));
            _balance = balance.Chests;
            while (State.Slots.Count < _balance.Slots)
            {
                State.Slots.Add(null);
            }
        }

        public ChestState State { get; }

        public ChestSlotStatus Status(int slot, long nowUnixMs)
        {
            ChestSlot chest = SlotOrNull(slot);
            if (chest == null)
            {
                return ChestSlotStatus.Empty;
            }
            if (chest.UnlockStartedUnixMs <= 0)
            {
                return ChestSlotStatus.Locked;
            }
            return SecondsLeft(slot, nowUnixMs) <= 0 ? ChestSlotStatus.Ready : ChestSlotStatus.Unlocking;
        }

        public int SecondsLeft(int slot, long nowUnixMs)
        {
            ChestSlot chest = SlotOrNull(slot);
            if (chest == null)
            {
                return 0;
            }
            if (chest.UnlockStartedUnixMs <= 0)
            {
                return chest.UnlockSeconds;
            }
            long end = chest.UnlockStartedUnixMs + chest.UnlockSeconds * 1000L;
            return (int)Math.Max(0, (end - nowUnixMs + 999) / 1000);
        }

        public int SkipCostOrbes(int slot, long nowUnixMs)
        {
            int left = SecondsLeft(slot, nowUnixMs);
            return left <= 0 ? 0 : Math.Max(1, (left + _balance.SkipSecondsPerOrbe - 1) / _balance.SkipSecondsPerOrbe);
        }

        /// <summary>Random rarity of a PvP victory chest.</summary>
        /// <summary>
        /// A victory chest, with a floor under bad luck. Pure weights mean a player can win twenty matches and see
        /// twenty wooden chests, and that is the run where they stop playing. A counter guarantees a gold chest at
        /// least every <see cref="ChestBalance.GoldAtLatest"/> wins and a crystal one every
        /// <see cref="ChestBalance.CrystalAtLatest"/>, so the worst streak anybody can have is a known one.
        ///
        /// The counters are only ever moved by <see cref="GrantVictoryChest"/>: rolling is left free of side effects
        /// so the server can preview a chest without changing what the player will get.
        /// </summary>
        public ChestType RollVictoryChest(DeterministicRandom rng)
        {
            if (_balance.CrystalAtLatest > 0 && State.SinceCrystal + 1 >= _balance.CrystalAtLatest)
            {
                return ChestType.Crystal;
            }
            if (_balance.GoldAtLatest > 0 && State.SinceGold + 1 >= _balance.GoldAtLatest)
            {
                return ChestType.Gold;
            }

            var weights = new List<int>();
            foreach (ChestDefinition def in _balance.Chests)
            {
                weights.Add(def.DropWeight);
            }
            return _balance.Chests[rng.NextWeightedIndex(weights)].Type;
        }

        /// <summary>Rolls a victory chest and moves the cycle counters on: the one call a match result should use.</summary>
        public ChestType GrantVictoryChest(DeterministicRandom rng)
        {
            ChestType type = RollVictoryChest(rng);
            State.SinceCrystal = type == ChestType.Crystal ? 0 : State.SinceCrystal + 1;
            State.SinceGold = type == ChestType.Crystal || type == ChestType.Gold ? 0 : State.SinceGold + 1;
            return type;
        }

        /// <summary>Wins left before the cycle guarantees a gold chest (0 when the next one is already it).</summary>
        public int WinsToGold() =>
            _balance.GoldAtLatest <= 0 ? -1 : Math.Max(0, _balance.GoldAtLatest - 1 - State.SinceGold);

        /// <summary>Wins left before the cycle guarantees a crystal chest (0 when the next one is already it).</summary>
        public int WinsToCrystal() =>
            _balance.CrystalAtLatest <= 0 ? -1 : Math.Max(0, _balance.CrystalAtLatest - 1 - State.SinceCrystal);

        // ------------------------------------------------------------------ the free chest

        /// <summary>Seconds before the free chest is ready; 0 when it is waiting to be taken.</summary>
        public int FreeChestSecondsLeft(long nowUnixMs)
        {
            if (_balance.FreeChestSeconds <= 0)
            {
                return int.MaxValue;
            }
            if (State.FreeTakenUnixMs <= 0)
            {
                // Never taken: it is ready, so the very first session has something to open.
                return 0;
            }
            long readyAt = State.FreeTakenUnixMs + (long)_balance.FreeChestSeconds * 1000;
            return (int)Math.Max(0, (readyAt - nowUnixMs + 999) / 1000);
        }

        public bool FreeChestReady(long nowUnixMs) => FreeChestSecondsLeft(nowUnixMs) == 0;

        /// <summary>
        /// Takes the free chest into a slot. It fails when the timer is still running or when every slot is full,
        /// and in the second case the timer is left alone so nothing is lost by tapping too early.
        /// </summary>
        public OperationResult<int> TakeFreeChest(long nowUnixMs)
        {
            if (!FreeChestReady(nowUnixMs))
            {
                return OperationResult<int>.Fail(ErrorCode.CooldownActive, "The free chest is not ready yet.");
            }
            int slot = Grant(_balance.FreeChestType, "free");
            if (slot < 0)
            {
                return OperationResult<int>.Fail(ErrorCode.LimitReached, "Every chest slot is full.");
            }
            State.FreeTakenUnixMs = nowUnixMs;
            return OperationResult<int>.Ok(slot);
        }

        /// <summary>Puts a chest in the first free slot. Returns the slot, or -1 when all slots are full (chest lost).</summary>
        public int Grant(ChestType type, string source, int? unlockSeconds = null)
        {
            for (int i = 0; i < State.Slots.Count; i++)
            {
                if (State.Slots[i] == null)
                {
                    State.Slots[i] = new ChestSlot { Type = type, UnlockSeconds = unlockSeconds ?? _balance.Get(type).UnlockSeconds, Source = source ?? string.Empty };
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Starts the timer of a locked chest (one chest unlocks at a time).</summary>
        public ErrorCode StartUnlock(int slot, long nowUnixMs)
        {
            if (Status(slot, nowUnixMs) != ChestSlotStatus.Locked)
            {
                return SlotOrNull(slot) == null ? ErrorCode.NotFound : ErrorCode.AlreadyClaimed;
            }
            for (int i = 0; i < State.Slots.Count; i++)
            {
                if (i != slot && Status(i, nowUnixMs) == ChestSlotStatus.Unlocking)
                {
                    return ErrorCode.LimitReached;
                }
            }
            State.Slots[slot].UnlockStartedUnixMs = nowUnixMs;
            return ErrorCode.None;
        }

        /// <summary>
        /// Opens a chest: ready ones for free, otherwise the caller must have charged <see cref="SkipCostOrbes"/> first
        /// (pass it in <paramref name="orbesPaid"/>). Rolls coins, power-ups, pet fragments and sometimes orbes.
        /// </summary>
        public OperationResult<ChestContent> Open(int slot, long nowUnixMs, int orbesPaid, DeterministicRandom rng, League highestLeague)
        {
            ChestSlot chest = SlotOrNull(slot);
            if (chest == null)
            {
                return OperationResult<ChestContent>.Fail(ErrorCode.NotFound);
            }
            int cost = SkipCostOrbes(slot, nowUnixMs);
            if (orbesPaid < cost)
            {
                return OperationResult<ChestContent>.Fail(ErrorCode.NotEnoughOrbes, "Chest not ready: " + cost + " orbes to open now.");
            }

            ChestDefinition def = _balance.Get(chest.Type);
            var content = new ChestContent { Type = chest.Type, OrbesSpent = cost };
            content.Reward.Coins = rng.NextInt(def.CoinsMin, def.CoinsMax + 1);
            if (def.OrbesChancePermille > 0 && rng.ChancePermille(def.OrbesChancePermille))
            {
                content.Reward.Orbes = def.Orbes;
            }

            var unlocked = new List<PowerUpType>();
            foreach (PowerUpDefinition p in _game.PowerUps.Definitions)
            {
                if (p.UnlockLeague <= highestLeague && !p.PvpOnly)
                {
                    unlocked.Add(p.Type);
                }
            }
            for (int i = 0; i < def.PowerUps && unlocked.Count > 0; i++)
            {
                content.Reward.AddPowerUp(unlocked[rng.NextInt(unlocked.Count)], 1);
            }

            if (def.PetFragmentsMax > 0 && _game.Pets.Pets.Count > 0)
            {
                content.FragmentsPet = _game.Pets.Pets[rng.NextInt(_game.Pets.Pets.Count)].Type;
                content.PetFragments = rng.NextInt(def.PetFragmentsMin, def.PetFragmentsMax + 1);
            }

            State.Slots[slot] = null;
            State.ChestsOpened++;
            return OperationResult<ChestContent>.Ok(content);
        }

        private ChestSlot SlotOrNull(int slot) => slot >= 0 && slot < State.Slots.Count ? State.Slots[slot] : null;
    }
}
