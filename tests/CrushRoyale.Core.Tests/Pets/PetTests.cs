using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Pets;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Tests.Gameplay;

namespace CrushRoyale.Core.Tests.Pets;

public class PetCollectionTests
{
    private static PetBalance Balance => Fixtures.Balance.Pets;

    [Fact]
    public void Summon_GuaranteesAPet_AtPity_AndEquipsTheFirstOne()
    {
        var pets = new PetCollection(new PetCollectionState(), Balance);
        var pulls = pets.Summon(Balance.PityPulls, new DeterministicRandom(7));

        Assert.Contains(pulls, p => p.WholePet);
        Assert.NotEqual(PetType.None, pets.State.Equipped);
        Assert.True(pets.Owns(pets.State.Equipped));
        Assert.Equal(1, pets.Get(pets.State.Equipped).Level);
        Assert.All(pulls.Where(p => !p.WholePet), p => Assert.InRange(p.Fragments, Balance.FragmentsMin, Balance.FragmentsMax));
    }

    [Fact]
    public void Summon_WholePetRate_IsAboutOneIn120()
    {
        var state = new PetCollectionState();
        var pets = new PetCollection(state, Balance);
        var rng = new DeterministicRandom(123);
        int whole = 0;
        const int pulls = 60000;
        for (int i = 0; i < pulls; i++)
        {
            state.PullsSinceWholePet = 0; // measure the raw odds, without pity
            whole += pets.Summon(1, rng).Count(p => p.WholePet);
        }
        double rate = whole / (double)pulls;
        Assert.InRange(rate, 1 / 150.0, 1 / 95.0);
    }

    [Fact]
    public void Duplicate_BecomesFragments()
    {
        var state = new PetCollectionState();
        state.Pets[PetType.FrostFox] = new PetState { Owned = true, Level = 1 };
        state.PullsSinceWholePet = Balance.PityPulls - 1;
        foreach (PetDefinition def in Balance.Pets)
        {
            state.Pets.TryAdd(def.Type, new PetState { Owned = true, Level = 1 });
        }
        var pets = new PetCollection(state, Balance);
        SummonPull pull = pets.Summon(1, new DeterministicRandom(5))[0];
        Assert.True(pull.WholePet && pull.Duplicate);
        Assert.Equal(Balance.DuplicateFragments, pull.Fragments);
    }

    [Fact]
    public void Xp_StopsAtGates_UntilAwakened()
    {
        var state = new PetCollectionState { Equipped = PetType.EmberSalamander };
        state.Pets[PetType.EmberSalamander] = new PetState { Owned = true, Level = 1 };
        var pets = new PetCollection(state, Balance);

        pets.AddXp(100000);
        PetState pet = pets.Get(PetType.EmberSalamander);
        Assert.Equal(2, pet.Level); // level 3 is a gate
        Assert.Equal(Balance.XpForLevel[2], pet.Xp); // XP not banked past the gate

        Assert.True(pets.CanAwaken(PetType.EmberSalamander, out int fragments, out int coins));
        Assert.Equal(Balance.GateFragments[0], fragments);
        Assert.Equal(Balance.GateCoins[0], coins);
        Assert.Equal(ErrorCode.NotEnoughItems, pets.Awaken(PetType.EmberSalamander));

        pet.Fragments = 1000;
        Assert.Equal(ErrorCode.None, pets.Awaken(PetType.EmberSalamander));
        Assert.Equal(3, pet.Level);
        pets.AddXp(100000);
        Assert.Equal(5, pet.Level); // next gate at 6
    }

    [Fact]
    public void Level_IsCappedInPvp_AndGiftOnlyAtMaxLevel()
    {
        SessionConfig pvp = SessionConfig.ForPvp(1, Fixtures.Balance, GameMode.PvpRanked, null, League.Bronze).WithPet(PetType.CrystalDrake, 10, Fixtures.Balance);
        Assert.Equal(Balance.PvpLevelCap, pvp.PetLevel);
        Assert.Null(new GameSession(pvp, Fixtures.Balance).PowerUps.PetGift);

        SessionConfig story = SessionConfig.ForStage(Fixtures.Stage(), Fixtures.Balance, null, League.Bronze).WithPet(PetType.EmberSalamander, 10, Fixtures.Balance);
        var session = new GameSession(story, Fixtures.Balance);
        Assert.Equal(PowerUpType.FireStorm, session.PowerUps.PetGift);
        Assert.Equal(ErrorCode.None, session.PowerUps.CanActivate(PowerUpType.FireStorm, false));

        SessionConfig lower = SessionConfig.ForStage(Fixtures.Stage(), Fixtures.Balance, null, League.Bronze).WithPet(PetType.EmberSalamander, 9, Fixtures.Balance);
        Assert.Null(new GameSession(lower, Fixtures.Balance).PowerUps.PetGift);
    }
}

public class PetInMatchTests
{
    private static SessionConfig Config(int level) =>
        SessionConfig.ForStage(Fixtures.Stage(moves: 60, target: 10_000_000, timeMs: 600000), Fixtures.Balance, null, League.Master)
            .WithPet(PetType.FrostFox, level, Fixtures.Balance);

    [Fact]
    public void PetMoves_AreFree_AndTheReplayVerifies()
    {
        SessionConfig config = Config(10);
        var session = new GameSession(config, Fixtures.Balance, "p");
        int petMoves = 0;
        while (session.IsRunning && session.MovesUsed < 60)
        {
            ActionOutcome outcome = Fixtures.PlayHint(session);
            Assert.True(outcome.Accepted);
            if (outcome.PetMove.HasValue)
            {
                petMoves++;
                Assert.True(outcome.PetPoints > 0);
            }
        }
        Assert.Equal(60, session.MovesUsed); // pet moves never spend a move
        Assert.InRange(petMoves, 1, 20);

        session.FinishByTime();
        byte[] bytes = ReplaySerializer.Serialize(session.Replay);
        ReplayData copy = ReplaySerializer.Deserialize(bytes);
        Assert.Equal(PetType.FrostFox, copy.Pet);
        Assert.Equal(10, copy.PetLevel);
        Assert.True(ReplaySimulator.Verify(copy, config, Fixtures.Balance).Valid);

        // Claiming the same replay without the pet must fail.
        SessionConfig noPet = SessionConfig.ForStage(Fixtures.Stage(moves: 60, target: 10_000_000, timeMs: 600000), Fixtures.Balance, null, League.Master);
        Assert.False(ReplaySimulator.Verify(copy, noPet, Fixtures.Balance).Valid);
    }

    [Fact]
    public void NoPet_NeverActs()
    {
        var session = new GameSession(Config(0), Fixtures.Balance, "p");
        for (int i = 0; i < 30 && session.IsRunning; i++)
        {
            Assert.Null(Fixtures.PlayHint(session).PetMove);
        }
    }
}
