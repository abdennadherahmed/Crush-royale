using System.Collections.Generic;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Core.Social
{
    /// <summary>One boss of the guild roster: a fixed name, its kingdom backdrop and the key of its epithet.</summary>
    public sealed class GuildBossIdentity
    {
        public GuildBossIdentity(string id, string name, Kingdom kingdom)
        {
            Id = id;
            Name = name;
            Kingdom = kingdom;
        }

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
            new GuildBossIdentity("7kou", "7kou", Kingdom.North),
            new GuildBossIdentity("escobaros", "Escobaros", Kingdom.Central),
            new GuildBossIdentity("majors_blue", "Majors Blue", Kingdom.West)
        };

        /// <summary>Boss of a weekly boss index (1-based, as stored in <see cref="GuildBossState.BossIndex"/>).</summary>
        public static GuildBossIdentity For(int bossIndex)
        {
            int index = bossIndex < 1 ? 0 : (bossIndex - 1) % All.Count;
            return All[index];
        }
    }
}
