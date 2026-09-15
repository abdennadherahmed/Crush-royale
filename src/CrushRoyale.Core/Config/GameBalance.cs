using System;
using CrushRoyale.Core.Common;

namespace CrushRoyale.Core.Config
{
    /// <summary>
    /// Root of every tunable number in the game ("no hardcoded values").
    /// Plain properties so the server can load it from JSON (remote config) and Unity from a TextAsset.
    /// Defaults = GDD values, except documented fixes (see docs/DESIGN_DECISIONS.md).
    /// The <see cref="ComputeHash"/> of the gameplay-relevant sections is embedded in replays so a
    /// client running an outdated balance can never be mistaken for a cheater.
    /// </summary>
    public sealed class GameBalance
    {
        /// <summary>Bump when a gameplay rule changes in code (not just numbers).</summary>
        public const int RulesVersion = 1;

        public BoardBalance Board { get; set; } = new BoardBalance();

        public ScoringBalance Scoring { get; set; } = new ScoringBalance();

        public ComboMeterBalance ComboMeter { get; set; } = new ComboMeterBalance();

        public TimingBalance Timing { get; set; } = new TimingBalance();

        public PowerUpBalance PowerUps { get; set; } = new PowerUpBalance();

        public PvpBalance Pvp { get; set; } = new PvpBalance();

        public TrophyBalance Trophies { get; set; } = new TrophyBalance();

        public MatchmakingBalance Matchmaking { get; set; } = new MatchmakingBalance();

        public StoryBalance Story { get; set; } = new StoryBalance();

        public StaminaBalance Stamina { get; set; } = new StaminaBalance();

        public EconomyBalance Economy { get; set; } = new EconomyBalance();

        public VipBalance Vip { get; set; } = new VipBalance();

        public GuildBalance Guild { get; set; } = new GuildBalance();

        public SocialBalance Social { get; set; } = new SocialBalance();

        public LiveOpsBalance LiveOps { get; set; } = new LiveOpsBalance();

        public AntiCheatBalance AntiCheat { get; set; } = new AntiCheatBalance();

        public static GameBalance CreateDefault() => new GameBalance();

        /// <summary>Throws if a value would break the simulation (called when loading remote config).</summary>
        public void Validate()
        {
            Board.Validate();
            Scoring.Validate();
            ComboMeter.Validate();
            Timing.Validate();
            PowerUps.Validate();
            Trophies.Validate();
            Story.Validate();
            Stamina.Validate();
            Economy.Validate();
            Vip.Validate();
            Guild.Validate();
        }

        /// <summary>Hash of every value that influences a match simulation (board, scoring, combo, timing, power-ups, pvp).</summary>
        public ulong ComputeHash()
        {
            var h = new HashBuilder();
            h.Add(RulesVersion);
            Board.AddToHash(h);
            Scoring.AddToHash(h);
            ComboMeter.AddToHash(h);
            Timing.AddToHash(h);
            PowerUps.AddToHash(h);
            h.Add(Pvp.TimeLimitMs).Add(Pvp.PowerUpsAllowed ? 1 : 0);
            return h.Value;
        }

        internal static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Invalid game balance: " + message);
            }
        }
    }
}
