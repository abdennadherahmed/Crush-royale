using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Pets
{
    /// <summary>One pet of the collection (fragments can exist before the pet itself is owned).</summary>
    public sealed class PetState
    {
        public bool Owned { get; set; }

        public int Level { get; set; }

        public int Xp { get; set; }

        public int Fragments { get; set; }
    }

    /// <summary>Persisted pet collection of a player.</summary>
    public sealed class PetCollectionState
    {
        public Dictionary<PetType, PetState> Pets { get; set; } = new Dictionary<PetType, PetState>();

        public PetType Equipped { get; set; }

        /// <summary>Pulls since the last whole pet (pity counter).</summary>
        public int PullsSinceWholePet { get; set; }

        public long TotalPulls { get; set; }
    }

    /// <summary>What one summon pull produced.</summary>
    public sealed class SummonPull
    {
        public PetType Pet { get; set; }

        /// <summary>True when the whole pet was drawn (a duplicate is converted into fragments).</summary>
        public bool WholePet { get; set; }

        public bool Duplicate { get; set; }

        public int Fragments { get; set; }
    }

    /// <summary>
    /// Pet rules shared by server and tests: summon odds with pity, auto-equip of the first pet, XP with awakening
    /// gates, and the level actually used in a match.
    /// </summary>
    public sealed class PetCollection
    {
        private readonly PetBalance _balance;

        public PetCollection(PetCollectionState state, PetBalance balance)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public PetCollectionState State { get; }

        public PetState Get(PetType type)
        {
            if (!State.Pets.TryGetValue(type, out PetState pet))
            {
                pet = new PetState();
                State.Pets[type] = pet;
            }
            return pet;
        }

        public bool Owns(PetType type) => type != PetType.None && State.Pets.TryGetValue(type, out PetState pet) && pet.Owned;

        /// <summary>Draws <paramref name="count"/> pulls. The caller charges the orbes first.</summary>
        public List<SummonPull> Summon(int count, DeterministicRandom rng)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }
            var pulls = new List<SummonPull>();
            for (int i = 0; i < count; i++)
            {
                PetDefinition def = _balance.Pets[rng.NextInt(_balance.Pets.Count)];
                State.TotalPulls++;
                State.PullsSinceWholePet++;
                bool whole = rng.NextInt(_balance.WholePetOneIn) == 0 || State.PullsSinceWholePet >= _balance.PityPulls;
                var pull = new SummonPull { Pet = def.Type, WholePet = whole };
                PetState pet = Get(def.Type);
                if (whole)
                {
                    State.PullsSinceWholePet = 0;
                    if (pet.Owned)
                    {
                        pull.Duplicate = true;
                        pull.Fragments = _balance.DuplicateFragments;
                        pet.Fragments += pull.Fragments;
                    }
                    else
                    {
                        pet.Owned = true;
                        pet.Level = 1;
                        pet.Xp = 0;
                        if (State.Equipped == PetType.None)
                        {
                            State.Equipped = def.Type;
                        }
                    }
                }
                else
                {
                    pull.Fragments = rng.NextInt(_balance.FragmentsMin, _balance.FragmentsMax + 1);
                    pet.Fragments += pull.Fragments;
                }
                pulls.Add(pull);
            }
            return pulls;
        }

        /// <summary>Unlocks a pet not owned yet by spending <see cref="PetBalance.UnlockFragments"/> of its fragments.</summary>
        public ErrorCode Unlock(PetType type)
        {
            if (type == PetType.None || _balance.Get(type) == null)
            {
                return ErrorCode.NotFound;
            }
            if (Owns(type))
            {
                return ErrorCode.AlreadyClaimed;
            }
            PetState pet = Get(type);
            if (pet.Fragments < _balance.UnlockFragments)
            {
                return ErrorCode.NotEnoughItems;
            }
            pet.Fragments -= _balance.UnlockFragments;
            pet.Owned = true;
            pet.Level = 1;
            pet.Xp = 0;
            if (State.Equipped == PetType.None)
            {
                State.Equipped = type;
            }
            return ErrorCode.None;
        }

        public ErrorCode Equip(PetType type)
        {
            if (type != PetType.None && !Owns(type))
            {
                return ErrorCode.NotFound;
            }
            State.Equipped = type;
            return ErrorCode.None;
        }

        /// <summary>Adds XP to the equipped pet; levels stop at an awakening gate until it is awakened. Returns levels gained.</summary>
        public int AddXp(int amount)
        {
            if (amount <= 0 || !Owns(State.Equipped))
            {
                return 0;
            }
            PetState pet = Get(State.Equipped);
            int before = pet.Level;
            pet.Xp += amount;
            LevelUpWhilePossible(pet);
            return pet.Level - before;
        }

        /// <summary>True when the pet has the XP for the next level but that level is an awakening gate.</summary>
        public bool CanAwaken(PetType type, out int fragments, out int coins)
        {
            fragments = 0;
            coins = 0;
            if (!Owns(type))
            {
                return false;
            }
            PetState pet = Get(type);
            if (pet.Level >= _balance.MaxLevel)
            {
                return false;
            }
            int gate = _balance.GateIndex(pet.Level + 1);
            if (gate < 0 || pet.Xp < _balance.XpForLevel[pet.Level])
            {
                return false;
            }
            fragments = _balance.GateFragments[gate];
            coins = _balance.GateCoins[gate];
            return true;
        }

        /// <summary>Crosses an awakening gate by spending the pet's fragments (the caller debits the coins first).</summary>
        public ErrorCode Awaken(PetType type)
        {
            if (!Owns(type))
            {
                return ErrorCode.NotFound;
            }
            if (!CanAwaken(type, out int fragments, out _))
            {
                return ErrorCode.LimitReached;
            }
            PetState pet = Get(type);
            if (pet.Fragments < fragments)
            {
                return ErrorCode.NotEnoughItems;
            }
            pet.Fragments -= fragments;
            pet.Level++;
            LevelUpWhilePossible(pet);
            return ErrorCode.None;
        }

        /// <summary>XP earned by the equipped pet for a finished match.</summary>
        public int XpFor(GameMode mode, bool won)
        {
            switch (mode)
            {
                case GameMode.Story:
                    return won ? _balance.StoryWinXp : _balance.StoryLossXp;
                case GameMode.GuildBoss:
                    return _balance.GuildBossXp;
                default:
                    return won ? _balance.PvpWinXp : _balance.PvpLossXp;
            }
        }

        /// <summary>Equipped pet and its level for a match config (None / 0 when nothing usable is equipped).</summary>
        public int MatchLevel(out PetType pet)
        {
            pet = Owns(State.Equipped) ? State.Equipped : PetType.None;
            return pet == PetType.None ? 0 : Math.Max(1, Math.Min(_balance.MaxLevel, Get(pet).Level));
        }

        private void LevelUpWhilePossible(PetState pet)
        {
            while (pet.Level < _balance.MaxLevel && pet.Xp >= _balance.XpForLevel[pet.Level] && _balance.GateIndex(pet.Level + 1) < 0)
            {
                pet.Level++;
            }
            // XP is not banked past a closed gate or past the max level.
            int cap = pet.Level >= _balance.MaxLevel ? _balance.XpForLevel[_balance.MaxLevel - 1] : _balance.XpForLevel[pet.Level];
            pet.Xp = Math.Min(pet.Xp, cap);
        }
    }
}
