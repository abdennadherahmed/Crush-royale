using System.Collections.Generic;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Core.Social
{
    /// <summary>
    /// How a guild boss actually fights.
    ///
    /// The three of them used to differ by portrait alone: the same board, the same rules, a different face. A boss
    /// is worth a week of a guild's attention only if beating it asks for something the others do not.
    /// </summary>
    public sealed class GuildBossRules
    {
        /// <summary>Countdown bombs kept on the board. One reaching zero ends the attack there and then.</summary>
        public int Bombs { get; set; }

        /// <summary>Moves a bomb starts with.</summary>
        public int BombFuse { get; set; }

        /// <summary>Share of the board corrupted from the first move, in permille of cells.</summary>
        public int StartBlightPermille { get; set; }

        /// <summary>Corruption covering this share of the board ends the attack. 0 = no such rule.</summary>
        public int BlightLossPermille { get; set; }

        /// <summary>Share of the board frozen from the first move, in permille of cells.</summary>
        public int StartIcePermille { get; set; }

        /// <summary>Ice covering this share of the board ends the attack. 0 = no such rule.</summary>
        public int IceLossPermille { get; set; }

        /// <summary>Cells frozen by a move that breaks nothing at all.</summary>
        public int IcePerWastedMove { get; set; }

        /// <summary>Layers each frozen cell starts with: two means one move to crack it and one more to clear it.</summary>
        public int IceLayers { get; set; } = 1;
    }

    /// <summary>One boss of the guild roster: a fixed name, its kingdom backdrop and the key of its epithet.</summary>
    public sealed class GuildBossIdentity
    {
        public GuildBossIdentity(string id, string name, Kingdom kingdom, GuildBossRules rules)
        {
            Id = id;
            Name = name;
            Kingdom = kingdom;
            Rules = rules ?? new GuildBossRules();
        }

        public GuildBossRules Rules { get; }

        /// <summary>Art file name (Resources/Art/Bosses/&lt;id&gt;) and localization suffix.</summary>
        public string Id { get; }

        /// <summary>Proper noun, identical in every language.</summary>
        public string Name { get; }

        public Kingdom Kingdom { get; }

        /// <summary>Localization key of the title shown under the name ("guildboss.7kou.title").</summary>
        public string TitleKey => "guildboss." + Id + ".title";
    }

    /// <summary>
    /// The guild bosses take turns week after week: the same three characters come back in order, so a guild builds a
    /// history with them instead of fighting an anonymous monster.
    /// </summary>
    public static class GuildBossRoster
    {
        public static readonly IReadOnlyList<GuildBossIdentity> All = new List<GuildBossIdentity>
        {
            // 7kou spreads corruption. A quarter of the board is rotten before the first move and it grows on every
            // move that destroys none of it; let it take half the board and the attack is over. The counter-play is
            // obvious and demanding at once: never stop clearing blight, even when a better match is available.
            new GuildBossIdentity("7kou", "7kou", Kingdom.North, new GuildBossRules
            {
                StartBlightPermille = 250,
                BlightLossPermille = 500
            }),

            // Escobaros freezes the board. Frozen cells take two moves to open -- one to crack the ice, one to clear
            // what is under it -- a quarter of the board starts frozen, and every move that breaks nothing at all
            // freezes three more cells. At three quarters frozen the attack ends. Hesitating is the way to lose.
            new GuildBossIdentity("escobaros", "Escobaros", Kingdom.Central, new GuildBossRules
            {
                StartIcePermille = 250,
                IceLossPermille = 750,
                IcePerWastedMove = 3,
                IceLayers = 2
            }),

            // Majors Blue plants bombs. Three on the board at once with a short fuse, and one reaching zero ends the
            // attack immediately, whatever the score was. Everything else can wait; the bombs cannot.
            new GuildBossIdentity("majors_blue", "Majors Blue", Kingdom.West, new GuildBossRules
            {
                Bombs = 3,
                BombFuse = 8
            })
        };

        /// <summary>Boss of a weekly boss index (1-based, as stored in <see cref="GuildBossState.BossIndex"/>).</summary>
        public static GuildBossIdentity For(int bossIndex)
        {
            int index = bossIndex < 1 ? 0 : (bossIndex - 1) % All.Count;
            return All[index];
        }
    }
}
