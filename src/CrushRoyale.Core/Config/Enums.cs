namespace CrushRoyale.Core.Config
{
    /// <summary>The 9 activatable power-ups from the GDD. Numeric values are persisted and sent over the network: never reorder.</summary>
    public enum PowerUpType : byte
    {
        ChronoBomb = 0,
        CoinBooster = 1,
        BrightSpark = 2,
        Multiplier2x = 3,
        GoldenChain = 4,
        FreezingGel = 5,
        NuclearBomb = 6,
        FireStorm = 7,
        CascadeInfinity = 8
    }

    public enum PowerUpTier : byte
    {
        Common = 1,
        Rare = 2,
        Epic = 3
    }

    /// <summary>PvP leagues, lowest to highest.</summary>
    public enum League : byte
    {
        Bronze = 0,
        Silver = 1,
        Gold = 2,
        Platinum = 3,
        Diamond = 4,
        Master = 5
    }

    public enum GameMode : byte
    {
        Story = 0,
        PvpRanked = 1,
        FriendlyChallenge = 2,
        GuildBoss = 3
    }

    public enum Currency : byte
    {
        Coins = 0,
        Orbes = 1
    }

    /// <summary>How the weekly (Sunday midnight UTC) trophy reset behaves.</summary>
    public enum SeasonResetPolicy : byte
    {
        /// <summary>GDD literal: everybody back to 0.</summary>
        Full = 0,

        /// <summary>Default: trophies above the floor are halved, so skill is preserved but the ladder stays fresh.</summary>
        Soft = 1
    }

    /// <summary>Companion pets (one per kingdom). Persisted and sent over the network: never reorder.</summary>
    public enum PetType : byte
    {
        None = 0,
        FrostFox = 1,
        SunFennec = 2,
        ForestOwl = 3,
        EmberSalamander = 4,
        CrystalDrake = 5
    }
}
