using System;
using System.Collections.Generic;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;

namespace CrushRoyale.Core.PowerUps
{
    /// <summary>A timed power-up window [StartMs, EndMs).</summary>
    public sealed class ActiveEffect
    {
        public ActiveEffect(PowerUpType type, int startMs, int endMs)
        {
            Type = type;
            StartMs = startMs;
            EndMs = endMs;
        }

        public PowerUpType Type { get; }

        public int StartMs { get; }

        public int EndMs { get; }

        public bool IsActiveAt(int timeMs) => timeMs >= StartMs && timeMs < EndMs;

        /// <summary>Move window (stages played in moves): StartMs/EndMs then hold move indices.</summary>
        public bool CountsMoves { get; private set; }

        public bool IsActiveAtMove(int movesUsed) => movesUsed >= StartMs && movesUsed < EndMs;

        public static ActiveEffect ForMoves(PowerUpType type, int startMove, int endMove) =>
            new ActiveEffect(type, startMove, endMove) { CountsMoves = true };
    }

    public readonly struct PowerUpPrice
    {
        public readonly int Coins;
        public readonly int Orbes;

        public PowerUpPrice(int coins, int orbes)
        {
            Coins = coins;
            Orbes = orbes;
        }
    }

    /// <summary>
    /// Task 4. Session-scoped power-up state (what was used, which effects are running) plus static rules
    /// for unlocks and prices shared with the shop.
    /// Effects:
    ///  - Chrono Bomb: +20 s to the timer.        - Coin Booster: score x1.5 for 20 s.
    ///  - Bright Spark: Match-3 spawns a Line bomb for 20 s.
    ///  - Multiplier x2: score x2 for 15 s.        - Golden Chain: first match also clears its row + column.
    ///  - Freezing Gel (PvP): opponent points scored in the next 10 s don't count (ghost-play adaptation).
    ///  - Nuclear Bomb: clears a 5x5 area.         - Fire Storm: clears every red and orange gem.
    ///  - Cascade Infinity: cascade multipliers x2 for 30 s.
    /// </summary>
    public sealed class PowerUpManager
    {
        private readonly GameBalance _balance;
        private readonly SessionConfig _config;
        private readonly Dictionary<PowerUpType, int> _remaining = new Dictionary<PowerUpType, int>();
        private readonly Dictionary<PowerUpType, int> _used = new Dictionary<PowerUpType, int>();
        private readonly List<ActiveEffect> _effects = new List<ActiveEffect>();

        public PowerUpManager(GameBalance balance, SessionConfig config)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            foreach (LoadoutEntry entry in config.Loadout)
            {
                _remaining[entry.Type] = entry.Quantity;
            }

            // Max-level pet: its signature power-up once per match, on top of the loadout and regardless of league.
            PetDefinition pet = config.Pet == PetType.None ? null : balance.Pets.Get(config.Pet);
            if (pet != null && config.PetLevel >= balance.Pets.PowerUpLevel && !balance.PowerUps.Get(pet.PowerUp).PvpOnly)
            {
                PetGift = pet.PowerUp;
                _remaining[pet.PowerUp] = Remaining(pet.PowerUp) + 1;
            }
        }

        /// <summary>Power-up offered by a max-level pet in this match, if any.</summary>
        public PowerUpType? PetGift { get; }

        public event Action<PowerUpType, int> PowerUpActivated;

        public bool GoldenChainArmed { get; private set; }

        public int ExtraTimeMs { get; private set; }

        /// <summary>Moves granted by Chrono Bomb on a stage played in moves, waiting to be added to the session.</summary>
        public int PendingExtraMoves { get; private set; }

        /// <summary>Moves already played, kept in sync by the session so move-based effect windows can be evaluated.</summary>
        public int MovesUsed { get; set; }

        /// <summary>True when the stage counts moves: boosters then last a number of moves instead of seconds.</summary>
        public bool CountsMoves => _config.HasMoveLimit;

        public int TakePendingExtraMoves()
        {
            int moves = PendingExtraMoves;
            PendingExtraMoves = 0;
            return moves;
        }

        public IReadOnlyList<ActiveEffect> Effects => _effects;

        public IReadOnlyDictionary<PowerUpType, int> UsedThisMatch => _used;

        public int Remaining(PowerUpType type) => _remaining.TryGetValue(type, out int n) ? n : 0;

        public int UsedCount(PowerUpType type) => _used.TryGetValue(type, out int n) ? n : 0;

        public bool InLoadout(PowerUpType type) => _remaining.ContainsKey(type);

        /// <summary>Can this power-up be activated now? (loadout, unlock, mode, once-per-match, stock, target).</summary>
        public ErrorCode CanActivate(PowerUpType type, bool hasTarget)
        {
            if (!Enum.IsDefined(typeof(PowerUpType), type))
            {
                return ErrorCode.InvalidArgument;
            }
            if (!InLoadout(type))
            {
                return ErrorCode.PowerUpNotInLoadout;
            }

            PowerUpDefinition def = _balance.PowerUps.Get(type);
            bool gift = PetGift == type;
            if (_config.HighestLeague < def.UnlockLeague && !gift)
            {
                return ErrorCode.PowerUpLocked;
            }
            if (def.PvpOnly && !_config.IsPvp)
            {
                return ErrorCode.PowerUpNotAvailableInMode;
            }
            if (def.OncePerMatch && UsedCount(type) >= (gift && InLoadoutFromConfig(type) ? 2 : 1))
            {
                return ErrorCode.PowerUpAlreadyUsed;
            }
            if (type == PowerUpType.GoldenChain && GoldenChainArmed)
            {
                return ErrorCode.PowerUpAlreadyUsed;
            }
            if (Remaining(type) <= 0)
            {
                return ErrorCode.NotEnoughItems;
            }
            if (def.NeedsTarget && !hasTarget)
            {
                return ErrorCode.PowerUpNeedsTarget;
            }
            return ErrorCode.None;
        }

        private bool InLoadoutFromConfig(PowerUpType type)
        {
            foreach (LoadoutEntry entry in _config.Loadout)
            {
                if (entry.Type == type)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Spends one unit and starts its effect. Call only after <see cref="CanActivate"/> succeeded.</summary>
        public void Consume(PowerUpType type, int nowMs)
        {
            ErrorCode check = CanActivate(type, true);
            if (check != ErrorCode.None && check != ErrorCode.PowerUpNeedsTarget)
            {
                throw new InvalidOperationException("Cannot activate " + type + ": " + check);
            }

            _remaining[type] = Remaining(type) - 1;
            _used[type] = UsedCount(type) + 1;

            PowerUpDefinition def = _balance.PowerUps.Get(type);
            switch (type)
            {
                case PowerUpType.ChronoBomb:
                    if (CountsMoves)
                    {
                        PendingExtraMoves += def.EffectMoves;
                    }
                    else
                    {
                        ExtraTimeMs += def.EffectValue;
                    }
                    break;
                case PowerUpType.GoldenChain:
                    GoldenChainArmed = true;
                    break;
                case PowerUpType.CoinBooster:
                case PowerUpType.BrightSpark:
                case PowerUpType.Multiplier2x:
                case PowerUpType.FreezingGel:
                case PowerUpType.CascadeInfinity:
                    _effects.Add(CountsMoves && def.DurationMoves > 0
                        ? ActiveEffect.ForMoves(type, MovesUsed, MovesUsed + def.DurationMoves)
                        : new ActiveEffect(type, nowMs, nowMs + def.DurationMs));
                    break;
            }

            PowerUpActivated?.Invoke(type, nowMs);
        }

        public void OnGoldenChainConsumed() => GoldenChainArmed = false;

        public bool IsEffectActive(PowerUpType type, int nowMs)
        {
            foreach (ActiveEffect e in _effects)
            {
                if (e.Type != type)
                {
                    continue;
                }
                if (e.CountsMoves ? e.IsActiveAtMove(MovesUsed) : e.IsActiveAt(nowMs))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Score multiplier from Coin Booster and Multiplier x2 (same-type windows don't stack).</summary>
        public int GetScoreMultiplierPermille(int nowMs)
        {
            long multiplier = 1000;
            if (IsEffectActive(PowerUpType.CoinBooster, nowMs))
            {
                multiplier = multiplier * _balance.PowerUps.Get(PowerUpType.CoinBooster).EffectValue / 1000;
            }
            if (IsEffectActive(PowerUpType.Multiplier2x, nowMs))
            {
                multiplier = multiplier * _balance.PowerUps.Get(PowerUpType.Multiplier2x).EffectValue / 1000;
            }
            return (int)multiplier;
        }

        public int GetCascadeBoostPermille(int nowMs) =>
            IsEffectActive(PowerUpType.CascadeInfinity, nowMs) ? _balance.PowerUps.Get(PowerUpType.CascadeInfinity).EffectValue : 1000;

        public bool IsBrightSparkActive(int nowMs) => IsEffectActive(PowerUpType.BrightSpark, nowMs);

        /// <summary>Freezing Gel windows, applied to the OPPONENT's score timeline when the PvP result is computed.</summary>
        public List<ActiveEffect> GetFreezeWindows()
        {
            var list = new List<ActiveEffect>();
            foreach (ActiveEffect e in _effects)
            {
                if (e.Type == PowerUpType.FreezingGel)
                {
                    list.Add(e);
                }
            }
            return list;
        }

        // ----------------------------------------------------------------- static rules

        /// <summary>Tier 1 always; Tier 2 once Silver was reached; Tier 3 once Gold was reached (highest league ever).</summary>
        public static bool IsUnlocked(GameBalance balance, PowerUpType type, League highestLeague)
        {
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }
            return highestLeague >= balance.PowerUps.Get(type).UnlockLeague;
        }

        /// <summary>Prompt API: can the player buy/equip this power-up id at all?</summary>
        public static bool IsPowerUpAvailable(GameBalance balance, int powerUpId, League highestLeague)
        {
            if (powerUpId < 0 || !Enum.IsDefined(typeof(PowerUpType), (PowerUpType)powerUpId))
            {
                return false;
            }
            return IsUnlocked(balance, (PowerUpType)powerUpId, highestLeague);
        }

        /// <summary>Prompt API: price in coins and orbes after the VIP discount (rounded up, never below 1).</summary>
        public static PowerUpPrice GetPowerUpPrice(GameBalance balance, PowerUpType type, int vipLevel)
        {
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }
            PowerUpDefinition def = balance.PowerUps.Get(type);
            int discount = Math.Min(balance.PowerUps.MaxVipDiscountPermille, Math.Max(0, vipLevel) * balance.PowerUps.VipDiscountPerLevelPermille);
            return new PowerUpPrice(ApplyDiscount(def.PriceCoins, discount), ApplyDiscount(def.PriceOrbes, discount));
        }

        public static PowerUpPrice GetPowerUpPrice(GameBalance balance, int powerUpId, int vipLevel)
        {
            if (powerUpId < 0 || !Enum.IsDefined(typeof(PowerUpType), (PowerUpType)powerUpId))
            {
                throw new ArgumentOutOfRangeException(nameof(powerUpId));
            }
            return GetPowerUpPrice(balance, (PowerUpType)powerUpId, vipLevel);
        }

        public static List<Pos> GetNuclearCells(GameBoard board, Pos center, int radius)
        {
            var cells = new List<Pos>();
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var p = new Pos(center.X + dx, center.Y + dy);
                    if (board.InBounds(p))
                    {
                        cells.Add(p);
                    }
                }
            }
            return cells;
        }

        public static List<Pos> GetFireStormCells(GameBoard board)
        {
            var cells = new List<Pos>();
            foreach (Pos p in board.AllPositions())
            {
                Piece piece = board[p];
                if (piece.IsMatchable && (piece.Color == PieceColor.Red || piece.Color == PieceColor.Orange))
                {
                    cells.Add(p);
                }
            }
            return cells;
        }

        private static int ApplyDiscount(int price, int discountPermille)
        {
            if (price <= 0)
            {
                return 0;
            }
            long discounted = ((long)price * (1000 - discountPermille) + 999) / 1000;
            return (int)Math.Max(1, discounted);
        }
    }
}
