using System;
using System.Collections.Generic;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Core.Gameplay
{
    /// <summary>A power-up brought into a match and how many units can be spent.</summary>
    public sealed class LoadoutEntry
    {
        public LoadoutEntry()
        {
        }

        public LoadoutEntry(PowerUpType type, int quantity)
        {
            Type = type;
            Quantity = quantity;
        }

        public PowerUpType Type { get; set; }

        public int Quantity { get; set; }
    }

    /// <summary>
    /// Everything needed to start (or re-simulate) a match. Built from StageData for story, from the balance for PvP / guild boss.
    /// </summary>
    public sealed class SessionConfig
    {
        /// <summary>PvP boards: medium opening, no obstacles (pure skill comparison).</summary>
        public const int PvpDifficultyPermille = 400;

        public const int GuildBossDifficultyPermille = 550;

        public const int GuildBossStones = 4;

        public const int MaxContinues = 5;

        public GameMode Mode { get; set; }

        public ulong Seed { get; set; }

        /// <summary>Story stage id, or guild boss index for GuildBoss, 0 for PvP.</summary>
        public int StageId { get; set; }

        public int TimeLimitMs { get; set; }

        /// <summary>0 = unlimited.</summary>
        public int MoveLimit { get; set; }

        public BoardGenerationOptions Board { get; set; } = new BoardGenerationOptions();

        public List<StageObjective> Objectives { get; set; } = new List<StageObjective>();

        public long BossHp { get; set; }

        public int BossPhases { get; set; }

        public int BossStonesPerPhase { get; set; }

        /// <summary>Countdown bombs kept on the board (story, from stage 101).</summary>
        public int TimeBombCount { get; set; }

        public int TimeBombMoves { get; set; }

        public int TwoStarScore { get; set; }

        public int ThreeStarScore { get; set; }

        public List<LoadoutEntry> Loadout { get; set; } = new List<LoadoutEntry>();

        /// <summary>Highest league ever reached: gates Rare/Epic power-ups.</summary>
        public League HighestLeague { get; set; }

        /// <summary>Extra moves granted after repeated failures on the same stage (see Story.DifficultyAssist).</summary>
        public int AssistExtraMoves { get; set; }

        /// <summary>Win-streak reward: special gems (line, bomb, line) already on the board when the stage starts.</summary>
        public int StartBoosters { get; set; }

        /// <summary>Equipped companion pet (None when absent).</summary>
        public PetType Pet { get; set; }

        /// <summary>Level the pet plays at in this match (already capped for PvP).</summary>
        public int PetLevel { get; set; }

        public bool IsPvp => Mode == GameMode.PvpRanked || Mode == GameMode.FriendlyChallenge;

        public bool HasMoveLimit => MoveLimit > 0;

        public const int MaxStartBoosters = 3;

        /// <summary>Adds the equipped pet, capping its level in PvP. Returns this config for chaining.</summary>
        public SessionConfig WithPet(PetType pet, int level, GameBalance balance)
        {
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }
            if (pet == PetType.None || level <= 0 || balance.Pets.Get(pet) == null)
            {
                Pet = PetType.None;
                PetLevel = 0;
                return this;
            }
            Pet = pet;
            PetLevel = Math.Min(level, IsPvp ? balance.Pets.PvpLevelCap : balance.Pets.MaxLevel);
            return this;
        }

        public static SessionConfig ForStage(StageData stage, GameBalance balance, IEnumerable<LoadoutEntry> loadout, League highestLeague, int assistExtraMoves = 0)
        {
            if (stage == null)
            {
                throw new ArgumentNullException(nameof(stage));
            }
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }

            return new SessionConfig
            {
                Mode = GameMode.Story,
                Seed = stage.Seed,
                StageId = stage.Id,
                TimeLimitMs = stage.TimeLimitMs,
                MoveLimit = stage.MoveLimit,
                Board = new BoardGenerationOptions
                {
                    Width = balance.Board.Width,
                    Height = balance.Board.Height,
                    ColorCount = stage.ColorCount,
                    DifficultyPermille = stage.DifficultyPermille,
                    StoneCount = stage.StoneCount,
                    StoneHp = stage.StoneHp,
                    IceCells = stage.IceCells,
                    IceLayers = stage.IceLayers,
                    BlightCount = stage.BlightCount,
                    EggCount = stage.EggCount,
                    MaxAttempts = balance.Board.MaxGenerationAttempts,
                    LowDifficultyBiasPermille = balance.Board.LowDifficultyBiasPermille
                },
                Objectives = new List<StageObjective>(stage.Objectives),
                BossHp = stage.BossHp,
                BossPhases = stage.BossPhases,
                BossStonesPerPhase = stage.BossStonesPerPhase,
                TimeBombCount = stage.TimeBombCount,
                TimeBombMoves = stage.TimeBombMoves,
                TwoStarScore = stage.TwoStarScore,
                ThreeStarScore = stage.ThreeStarScore,
                Loadout = CopyLoadout(loadout),
                HighestLeague = highestLeague,
                AssistExtraMoves = Math.Max(0, assistExtraMoves)
            };
        }

        /// <summary>Win-streak starting bonuses (0-3). Returns this config for chaining.</summary>
        public SessionConfig WithStartBoosters(int count)
        {
            StartBoosters = Math.Max(0, Math.Min(MaxStartBoosters, count));
            return this;
        }

        public static SessionConfig ForPvp(ulong seed, GameBalance balance, GameMode mode, IEnumerable<LoadoutEntry> loadout, League highestLeague)
        {
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }
            if (mode != GameMode.PvpRanked && mode != GameMode.FriendlyChallenge)
            {
                throw new ArgumentException("Mode must be a PvP mode.", nameof(mode));
            }

            return new SessionConfig
            {
                Mode = mode,
                Seed = seed,
                StageId = 0,
                TimeLimitMs = balance.Pvp.TimeLimitMs,
                MoveLimit = 0,
                Board = new BoardGenerationOptions
                {
                    Width = balance.Board.Width,
                    Height = balance.Board.Height,
                    ColorCount = balance.Board.ColorCount,
                    DifficultyPermille = PvpDifficultyPermille,
                    MaxAttempts = balance.Board.MaxGenerationAttempts,
                    LowDifficultyBiasPermille = balance.Board.LowDifficultyBiasPermille
                },
                Loadout = balance.Pvp.PowerUpsAllowed ? CopyLoadout(loadout) : new List<LoadoutEntry>(),
                HighestLeague = highestLeague
            };
        }

        public static SessionConfig ForGuildBoss(ulong seed, int bossIndex, GameBalance balance, IEnumerable<LoadoutEntry> loadout, League highestLeague)
        {
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }
            if (bossIndex < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bossIndex));
            }

            return new SessionConfig
            {
                Mode = GameMode.GuildBoss,
                Seed = seed,
                StageId = bossIndex,
                TimeLimitMs = balance.Pvp.TimeLimitMs,
                MoveLimit = 0,
                Board = new BoardGenerationOptions
                {
                    Width = balance.Board.Width,
                    Height = balance.Board.Height,
                    ColorCount = balance.Board.ColorCount,
                    DifficultyPermille = GuildBossDifficultyPermille,
                    StoneCount = GuildBossStones,
                    MaxAttempts = balance.Board.MaxGenerationAttempts,
                    LowDifficultyBiasPermille = balance.Board.LowDifficultyBiasPermille
                },
                Loadout = CopyLoadout(loadout),
                HighestLeague = highestLeague
            };
        }

        /// <summary>Checks loadout rules (slots, once-per-match, unlocks, mode restrictions) and structural sanity.</summary>
        public ErrorCode Validate(GameBalance balance)
        {
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }
            if (TimeLimitMs <= 0 || MoveLimit < 0 || Board == null)
            {
                return ErrorCode.InvalidArgument;
            }
            if (Board.Width != balance.Board.Width || Board.ColorCount < 3 || Board.ColorCount > 6)
            {
                return ErrorCode.InvalidArgument;
            }
            if (Mode == GameMode.Story && (Objectives == null || Objectives.Count == 0))
            {
                return ErrorCode.InvalidArgument;
            }
            if (BossPhases < 0 || BossStonesPerPhase < 0 || BossHp < 0)
            {
                return ErrorCode.InvalidArgument;
            }
            if (TimeBombCount < 0 || TimeBombCount > 6 || (TimeBombCount > 0 && (TimeBombMoves < 3 || TimeBombMoves > 60)))
            {
                return ErrorCode.InvalidArgument;
            }
            if ((Pet == PetType.None) != (PetLevel == 0) || PetLevel < 0 || PetLevel > (IsPvp ? balance.Pets.PvpLevelCap : balance.Pets.MaxLevel)
                || (Pet != PetType.None && balance.Pets.Get(Pet) == null))
            {
                return ErrorCode.InvalidArgument;
            }

            var seen = new HashSet<PowerUpType>();
            foreach (LoadoutEntry entry in Loadout ?? new List<LoadoutEntry>())
            {
                if (entry == null || entry.Quantity <= 0 || !Enum.IsDefined(typeof(PowerUpType), entry.Type))
                {
                    return ErrorCode.InvalidArgument;
                }
                if (!seen.Add(entry.Type))
                {
                    return ErrorCode.InvalidArgument;
                }

                PowerUpDefinition def = balance.PowerUps.Get(entry.Type);
                if (def.OncePerMatch && entry.Quantity > 1)
                {
                    return ErrorCode.LimitReached;
                }
                if (HighestLeague < def.UnlockLeague)
                {
                    return ErrorCode.PowerUpLocked;
                }
                if (def.PvpOnly && !IsPvp)
                {
                    return ErrorCode.PowerUpNotAvailableInMode;
                }
            }
            if (seen.Count > balance.PowerUps.LoadoutSlots)
            {
                return ErrorCode.LimitReached;
            }
            return ErrorCode.None;
        }

        private static List<LoadoutEntry> CopyLoadout(IEnumerable<LoadoutEntry> loadout)
        {
            var list = new List<LoadoutEntry>();
            if (loadout != null)
            {
                foreach (LoadoutEntry e in loadout)
                {
                    if (e != null)
                    {
                        list.Add(new LoadoutEntry(e.Type, e.Quantity));
                    }
                }
            }
            return list;
        }
    }
}
