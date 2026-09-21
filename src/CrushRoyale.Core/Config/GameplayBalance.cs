using System.Collections.Generic;
using CrushRoyale.Core.Common;

namespace CrushRoyale.Core.Config
{
    /// <summary>Board size and generation (GDD: 8x8, 6 colors, 3-4 opening moves).</summary>
    public sealed class BoardBalance
    {
        public int Width { get; set; } = 8;

        public int Height { get; set; } = 8;

        public int ColorCount { get; set; } = 6;

        /// <summary>Boards tried before keeping the closest candidate.</summary>
        public int MaxGenerationAttempts { get; set; } = 400;

        /// <summary>At 0% difficulty, chance to place a gem that sets up a near-match (fades to 0 at 100%).</summary>
        public int LowDifficultyBiasPermille { get; set; } = 350;

        /// <summary>Safety cap on cascade steps for a single action.</summary>
        public int MaxResolutionSteps { get; set; } = 500;

        internal void Validate()
        {
            GameBalance.Require(Width >= 5 && Width <= 16, "Board.Width");
            GameBalance.Require(Height >= 5 && Height <= 16, "Board.Height");
            GameBalance.Require(ColorCount >= 3 && ColorCount <= 6, "Board.ColorCount");
            GameBalance.Require(MaxGenerationAttempts >= 1, "Board.MaxGenerationAttempts");
            GameBalance.Require(MaxResolutionSteps >= 10, "Board.MaxResolutionSteps");
        }

        internal void AddToHash(HashBuilder h)
        {
            h.Add(Width).Add(Height).Add(ColorCount).Add(MaxGenerationAttempts).Add(LowDifficultyBiasPermille).Add(MaxResolutionSteps);
        }
    }

    /// <summary>Points. All multipliers are integers in permille (1000 = x1) to keep the simulation bit-exact.</summary>
    public sealed class ScoringBalance
    {
        public int PointsPerPiece { get; set; } = 20;

        public int Line4Bonus { get; set; } = 60;

        public int CrossBonus { get; set; } = 150;

        /// <summary>GDD "Match 5 ... + super bonus".</summary>
        public int Line5SuperBonus { get; set; } = 500;

        public int SpecialActivationBonus { get; set; } = 50;

        public int StonePoints { get; set; } = 50;

        public int IceLayerPoints { get; set; } = 40;

        /// <summary>
        /// Indexed by cascade level. Level 0 = the player's own match; levels 1..4+ follow the GDD table:
        /// L1 x1, L2 x1.5, L3 x2, L4+ x3 (MEGA CASCADE).
        /// </summary>
        public int[] CascadeMultipliersPermille { get; set; } = { 1000, 1000, 1500, 2000, 3000 };

        public int MegaCascadeLevel { get; set; } = 4;

        /// <summary>GDD "each cascade adds +10% score stacking" (applied to the whole action).</summary>
        public int ChainBonusPerCascadePermille { get; set; } = 100;

        public int MaxChainBonusPermille { get; set; } = 1000;

        /// <summary>Upper bound for stacked multipliers (Red Surge x Multiplier x2 x Coin Booster).</summary>
        public int MaxScoreMultiplierPermille { get; set; } = 5000;

        /// <summary>Story: points per unused move when the stage is won early.</summary>
        /// <summary>Bonus points for a 2x2 square match (between a Line4 and an L/T cross).</summary>
        public int SquareBonus { get; set; } = 120;

        public int RemainingMoveBonus { get; set; } = 150;

        /// <summary>Story: points per unused full second when the stage is won early.</summary>
        public int RemainingSecondBonus { get; set; } = 20;

        /// <summary>Share of the moves (or of the clock) left that already earns the 2nd / 3rd star, whatever the score.</summary>
        public int TwoStarSparePermille { get; set; } = 250;

        public int ThreeStarSparePermille { get; set; } = 450;

        public int GetCascadeMultiplierPermille(int cascadeLevel)
        {
            if (cascadeLevel < 0)
            {
                cascadeLevel = 0;
            }
            int[] table = CascadeMultipliersPermille;
            return table[cascadeLevel < table.Length ? cascadeLevel : table.Length - 1];
        }

        internal void Validate()
        {
            GameBalance.Require(PointsPerPiece > 0, "Scoring.PointsPerPiece");
            GameBalance.Require(CascadeMultipliersPermille != null && CascadeMultipliersPermille.Length >= 2, "Scoring.CascadeMultipliersPermille");
            foreach (int m in CascadeMultipliersPermille)
            {
                GameBalance.Require(m >= 1000, "Scoring.CascadeMultipliersPermille must be >= 1000");
            }
            GameBalance.Require(MaxScoreMultiplierPermille >= 1000, "Scoring.MaxScoreMultiplierPermille");
        }

        internal void AddToHash(HashBuilder h)
        {
            h.Add(PointsPerPiece).Add(Line4Bonus).Add(CrossBonus).Add(Line5SuperBonus).Add(SpecialActivationBonus)
                .Add(StonePoints).Add(IceLayerPoints).Add(MegaCascadeLevel).Add(ChainBonusPerCascadePermille)
                .Add(MaxChainBonusPermille).Add(MaxScoreMultiplierPermille).Add(RemainingMoveBonus).Add(RemainingSecondBonus)
                .Add(TwoStarSparePermille).Add(ThreeStarSparePermille).Add(SquareBonus);
            foreach (int m in CascadeMultipliersPermille)
            {
                h.Add(m);
            }
        }
    }

    /// <summary>Red Surge combo meter (GDD "Combo Meter System").</summary>
    public sealed class ComboMeterBalance
    {
        public int Line3Fill { get; set; } = 100;

        public int Line4Fill { get; set; } = 200;

        public int CrossFill { get; set; } = 250;

        public int Line5Fill { get; set; } = 350;

        /// <summary>
        /// GDD says +50% per cascade, which keeps Red Surge permanently on (two cascades = full meter).
        /// Reduced to +15% by default; set back to 500 in remote config to test the GDD value.
        /// </summary>
        public int CascadeFill { get; set; } = 150;

        public int MaxPermille { get; set; } = 1000;

        public int SurgeDurationMs { get; set; } = 20000;

        public int SurgeMultiplierPermille { get; set; } = 2000;

        internal void Validate()
        {
            GameBalance.Require(MaxPermille > 0, "ComboMeter.MaxPermille");
            GameBalance.Require(SurgeDurationMs > 0, "ComboMeter.SurgeDurationMs");
            GameBalance.Require(SurgeMultiplierPermille >= 1000, "ComboMeter.SurgeMultiplierPermille");
        }

        internal void AddToHash(HashBuilder h)
        {
            h.Add(Line3Fill).Add(Line4Fill).Add(CrossFill).Add(Line5Fill).Add(CascadeFill).Add(MaxPermille).Add(SurgeDurationMs).Add(SurgeMultiplierPermille);
        }
    }

    /// <summary>
    /// Animation-driven timings. The client locks input while an action animates, so the next action can
    /// never legitimately start earlier than the previous action's duration: the server uses this to catch bots.
    /// </summary>
    public sealed class TimingBalance
    {
        public int SwapAnimationMs { get; set; } = 250;

        public int CascadeStepMs { get; set; } = 400;

        public int PowerUpAnimationMs { get; set; } = 600;

        public int ShuffleAnimationMs { get; set; } = 800;

        public int InvalidSwapAnimationMs { get; set; } = 300;

        /// <summary>GDD anti-cheat "minimum time between moves (e.g. 200ms)".</summary>
        public int MinActionIntervalMs { get; set; } = 200;

        /// <summary>Clock jitter accepted on top of animation durations.</summary>
        public int TimingToleranceMs { get; set; } = 80;

        public int TimeWarningMs { get; set; } = 10000;

        /// <summary>Replay board-hash checkpoint interval ("snapshots every 5 seconds").</summary>
        public int CheckpointIntervalMs { get; set; } = 5000;

        /// <summary>Ghost moves are shown slightly behind real time.</summary>
        public int GhostPlaybackDelayMs { get; set; } = 500;

        internal void Validate()
        {
            GameBalance.Require(MinActionIntervalMs >= 0, "Timing.MinActionIntervalMs");
            GameBalance.Require(CheckpointIntervalMs >= 1000, "Timing.CheckpointIntervalMs");
            GameBalance.Require(TimeWarningMs > 0, "Timing.TimeWarningMs");
        }

        internal void AddToHash(HashBuilder h)
        {
            h.Add(SwapAnimationMs).Add(CascadeStepMs).Add(PowerUpAnimationMs).Add(ShuffleAnimationMs).Add(InvalidSwapAnimationMs)
                .Add(MinActionIntervalMs).Add(TimingToleranceMs).Add(TimeWarningMs).Add(CheckpointIntervalMs);
        }
    }

    /// <summary>Static definition of one power-up.</summary>
    public sealed class PowerUpDefinition
    {
        public PowerUpType Type { get; set; }

        public PowerUpTier Tier { get; set; }

        /// <summary>
        /// How long the effect lasts on a stage played in MOVES (0 = the effect is not a window). Stages with a move
        /// limit ignore <see cref="DurationMs"/>: a timer nobody can see made every booster feel useless.
        /// </summary>
        public int DurationMoves { get; set; }

        /// <summary>Moves granted instead of time on a stage played in moves (Chrono Bomb).</summary>
        public int EffectMoves { get; set; }

        public int PriceCoins { get; set; }

        /// <summary>Alternative price in Orbes (shop shows both when relevant).</summary>
        public int PriceOrbes { get; set; }

        /// <summary>Effect duration (0 = instant).</summary>
        public int DurationMs { get; set; }

        /// <summary>Multiplier/strength of the effect in permille, or extra milliseconds for Chrono Bomb.</summary>
        public int EffectValue { get; set; }

        /// <summary>GDD "Cooldown: 1x per match".</summary>
        public bool OncePerMatch { get; set; }

        public bool PvpOnly { get; set; }

        public bool NeedsTarget { get; set; }

        /// <summary>Highest league ever reached required to unlock (Tier 2 = Silver, Tier 3 = Gold).</summary>
        public League UnlockLeague { get; set; }
    }

    public sealed class PowerUpBalance
    {
        /// <summary>Power-ups a player can bring into one match (pre-game selection).</summary>
        public int LoadoutSlots { get; set; } = 3;

        /// <summary>Nuclear Bomb radius: 2 => 5x5.</summary>
        public int NuclearRadius { get; set; } = 2;

        /// <summary>
        /// VIP discount on power-up prices per VIP level. The prompt says 10%/level, which makes everything
        /// free at VIP 10; capped at 3%/level (max 30%) to keep coins meaningful.
        /// </summary>
        public int VipDiscountPerLevelPermille { get; set; } = 30;

        public int MaxVipDiscountPermille { get; set; } = 300;

        public List<PowerUpDefinition> Definitions { get; set; } = new List<PowerUpDefinition>
        {
            new PowerUpDefinition { Type = PowerUpType.ChronoBomb, Tier = PowerUpTier.Common, PriceCoins = 50, PriceOrbes = 5, EffectValue = 20000, EffectMoves = 4, UnlockLeague = League.Bronze },
            new PowerUpDefinition { Type = PowerUpType.CoinBooster, Tier = PowerUpTier.Common, PriceCoins = 60, PriceOrbes = 6, DurationMs = 20000, DurationMoves = 5, EffectValue = 1500, UnlockLeague = League.Bronze },
            new PowerUpDefinition { Type = PowerUpType.BrightSpark, Tier = PowerUpTier.Common, PriceCoins = 70, PriceOrbes = 7, DurationMs = 20000, DurationMoves = 5, UnlockLeague = League.Bronze },
            new PowerUpDefinition { Type = PowerUpType.Multiplier2x, Tier = PowerUpTier.Rare, PriceCoins = 200, PriceOrbes = 20, DurationMs = 15000, DurationMoves = 4, EffectValue = 2000, OncePerMatch = true, UnlockLeague = League.Silver },
            new PowerUpDefinition { Type = PowerUpType.GoldenChain, Tier = PowerUpTier.Rare, PriceCoins = 180, PriceOrbes = 18, OncePerMatch = true, UnlockLeague = League.Silver },
            new PowerUpDefinition { Type = PowerUpType.FreezingGel, Tier = PowerUpTier.Rare, PriceCoins = 220, PriceOrbes = 22, DurationMs = 10000, EffectValue = 1000, OncePerMatch = true, PvpOnly = true, UnlockLeague = League.Silver },
            new PowerUpDefinition { Type = PowerUpType.NuclearBomb, Tier = PowerUpTier.Epic, PriceCoins = 500, PriceOrbes = 50, OncePerMatch = true, NeedsTarget = true, UnlockLeague = League.Gold },
            new PowerUpDefinition { Type = PowerUpType.FireStorm, Tier = PowerUpTier.Epic, PriceCoins = 480, PriceOrbes = 48, OncePerMatch = true, UnlockLeague = League.Gold },
            new PowerUpDefinition { Type = PowerUpType.CascadeInfinity, Tier = PowerUpTier.Epic, PriceCoins = 550, PriceOrbes = 55, DurationMs = 30000, DurationMoves = 4, EffectValue = 2000, OncePerMatch = true, UnlockLeague = League.Gold }
        };

        public PowerUpDefinition Get(PowerUpType type)
        {
            foreach (PowerUpDefinition d in Definitions)
            {
                if (d.Type == type)
                {
                    return d;
                }
            }
            throw new KeyNotFoundException("No definition for power-up " + type);
        }

        internal void Validate()
        {
            GameBalance.Require(LoadoutSlots >= 1 && LoadoutSlots <= 9, "PowerUps.LoadoutSlots");
            GameBalance.Require(NuclearRadius >= 1, "PowerUps.NuclearRadius");
            foreach (PowerUpType t in (PowerUpType[])System.Enum.GetValues(typeof(PowerUpType)))
            {
                PowerUpDefinition d = Get(t);
                GameBalance.Require(d.PriceCoins > 0, "PowerUps " + t + " price");
            }
        }

        internal void AddToHash(HashBuilder h)
        {
            h.Add(LoadoutSlots).Add(NuclearRadius);
            foreach (PowerUpDefinition d in Definitions)
            {
                h.Add((int)d.Type).Add(d.DurationMs).Add(d.EffectValue).Add(d.OncePerMatch ? 1 : 0).Add(d.PvpOnly ? 1 : 0).Add(d.NeedsTarget ? 1 : 0).Add(d.DurationMoves).Add(d.EffectMoves);
            }
        }
    }

    public sealed class PvpBalance
    {
        /// <summary>Both players get the same time on the same seed; highest score wins.</summary>
        public int TimeLimitMs { get; set; } = 90000;

        public bool PowerUpsAllowed { get; set; } = true;

        /// <summary>Coins for playing a ranked match.</summary>
        public int WinCoins { get; set; } = 120;

        public int LossCoins { get; set; } = 40;

        public int DrawCoins { get; set; } = 60;

        /// <summary>Coins paid to the owner of a ghost that successfully "defended".</summary>
        public int GhostDefenseCoins { get; set; } = 20;
    }
}
