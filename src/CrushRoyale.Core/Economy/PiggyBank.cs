using System;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Economy
{
    /// <summary>
    /// The piggy bank: a share of what the player earns by playing is dropped into a jar they can see but not open.
    /// Breaking it is a real-money purchase.
    ///
    /// It works because it is not a shop offer, it is a record of what the player has already done. The orbes inside
    /// were earned by their own matches, so the price buys something that already feels theirs, and watching it fill
    /// is a reason to come back that costs nothing to give.
    ///
    /// The jar stops filling once it is full. That is the point: a full jar is what makes the offer worth looking at,
    /// and letting it keep swallowing coins after that would turn a reward into a tax.
    /// </summary>
    public sealed class PiggyBankState
    {
        public long Orbes { get; set; }

        /// <summary>How many times it has been broken, kept for the shop to show and for analytics.</summary>
        public int TimesBroken { get; set; }
    }

    public sealed class PiggyBankBalance
    {
        /// <summary>Orbes dropped in for each stage cleared.</summary>
        public int OrbesPerStageWin { get; set; } = 3;

        /// <summary>Orbes dropped in for each arena win.</summary>
        public int OrbesPerArenaWin { get; set; } = 5;

        /// <summary>Full at this much: the jar stops taking, and the offer starts being worth looking at.</summary>
        public int CapOrbes { get; set; } = 600;

        /// <summary>Store product that breaks it open.</summary>
        public string Sku { get; set; } = "crushroyale.piggybank";

        public int PriceCents { get; set; } = 499;

        /// <summary>Below this the jar is not worth offering, so the shop hides it.</summary>
        public int MinOrbesToOffer { get; set; } = 120;

        internal void Validate()
        {
            GameBalance.Require(CapOrbes > 0, "PiggyBank.CapOrbes must be positive.");
            GameBalance.Require(MinOrbesToOffer <= CapOrbes, "PiggyBank.MinOrbesToOffer above the cap would never offer it.");
            GameBalance.Require(PriceCents > 0, "PiggyBank.PriceCents");
            GameBalance.Require(!string.IsNullOrWhiteSpace(Sku), "PiggyBank.Sku");
        }
    }

    public static class PiggyBank
    {
        /// <summary>Drops orbes in and returns what actually went in, which is less than asked once it is full.</summary>
        public static int Fill(PiggyBankState state, int orbes, PiggyBankBalance balance)
        {
            if (state == null || balance == null || orbes <= 0)
            {
                return 0;
            }
            long room = Math.Max(0, balance.CapOrbes - state.Orbes);
            int added = (int)Math.Min(orbes, room);
            state.Orbes += added;
            return added;
        }

        public static bool IsFull(PiggyBankState state, PiggyBankBalance balance) =>
            state != null && balance != null && state.Orbes >= balance.CapOrbes;

        /// <summary>True once there is enough inside to be worth an offer.</summary>
        public static bool CanOffer(PiggyBankState state, PiggyBankBalance balance) =>
            state != null && balance != null && state.Orbes >= balance.MinOrbesToOffer;

        /// <summary>
        /// Empties the jar and returns what was inside. The caller has already taken the payment; this only ever
        /// hands over orbes the player earned, so it can never pay out more than was put in.
        /// </summary>
        public static long Break(PiggyBankState state)
        {
            if (state == null || state.Orbes <= 0)
            {
                return 0;
            }
            long contents = state.Orbes;
            state.Orbes = 0;
            state.TimesBroken++;
            return contents;
        }
    }
}
