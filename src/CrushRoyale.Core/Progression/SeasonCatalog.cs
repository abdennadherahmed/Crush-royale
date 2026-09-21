using System;
using System.Collections.Generic;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Core.Progression
{
    /// <summary>One battle pass season: authored, not derived from the clock.</summary>
    public sealed class SeasonDefinition
    {
        /// <summary>Stable id used in save data and telemetry ("beta", "s1", "s13" for the encore seasons).</summary>
        public string Id { get; internal set; }

        /// <summary>0 for the beta season, then 1..12, then 13, 14, ... for the encore seasons.</summary>
        public int Index { get; internal set; }

        /// <summary>Localization key of the display name shown in the battle pass header.</summary>
        public string NameKey { get; internal set; }

        public Kingdom Theme { get; internal set; }

        public DateTime StartUtc { get; internal set; }

        public int DurationDays { get; internal set; }

        /// <summary>The one cosmetic that can only ever be earned on this season's premium track.</summary>
        public string ExclusiveCosmeticId { get; internal set; }

        public DateTime EndUtc => StartUtc.AddDays(DurationDays);

        /// <summary>The beta season is the pre-launch one: the client shows it as "Beta", never as a number.</summary>
        public bool IsBeta => Index == 0;

        /// <summary>
        /// Display name: Loc.T(NameKey, Index). Authored names ignore the argument; only the encore key uses it, so a
        /// single call works for every season.
        /// </summary>
        public override string ToString() => Id + " (" + Theme + ")";
    }

    /// <summary>
    /// The authored season schedule. The season index used to be "days since the Unix epoch / 28", which is why the
    /// battle pass advertised season 739; it is now a position in this list, so the launch season really is season 1.
    /// Everything here is a pure function of a UTC date, so client and server always agree without a round trip.
    /// </summary>
    public static class SeasonCatalog
    {
        /// <summary>Beta opens here; anything earlier is clamped to the beta season so a dev clock never breaks the UI.</summary>
        public static readonly DateTime BetaStartUtc = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Launch day: the beta ends and season 1 starts, both on a Monday 00:00 UTC.</summary>
        public static readonly DateTime LaunchUtc = new DateTime(2027, 1, 4, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Live seasons are 4 weeks, like the old epoch-derived ones.</summary>
        public const int SeasonDays = 28;

        // Underscore, not a dot: the client turns the id into a Resources path (Art/Characters/hero_full_{suffix}).
        public const string CosmeticPrefix = "outfit.season_";

        /// <summary>The beta season plus seasons 1..12.</summary>
        public static readonly IReadOnlyList<SeasonDefinition> All = Build();

        /// <summary>Last authored season; after it the catalog loops (see <see cref="At"/>).</summary>
        public static SeasonDefinition Last => All[All.Count - 1];

        /// <summary>
        /// Encore seasons reuse the themes and skins of the last <see cref="EncoreLength"/> authored seasons, with a
        /// fresh id and a number that keeps counting up, so the pass never runs out and never shows a nonsense season.
        /// </summary>
        public const int EncoreLength = 4;

        /// <summary>The season live at this instant, authored or encore. Never null, never a nonsense index.</summary>
        public static SeasonDefinition At(DateTime utc)
        {
            DateTime now = Normalize(utc);
            if (now < BetaStartUtc)
            {
                return All[0];
            }
            for (int i = 0; i < All.Count; i++)
            {
                if (now < All[i].EndUtc)
                {
                    return All[i];
                }
            }

            // Past season 12: keep rolling 28-day seasons, numbered 13, 14, ... and themed on the last four.
            int elapsed = (int)Math.Floor((now - Last.EndUtc).TotalDays / SeasonDays);
            int index = Last.Index + 1 + elapsed;
            SeasonDefinition source = All[All.Count - EncoreLength + (elapsed % EncoreLength)];
            return new SeasonDefinition
            {
                Id = "s" + index,
                Index = index,
                NameKey = "season.encore",
                Theme = source.Theme,
                StartUtc = Last.EndUtc.AddDays((double)elapsed * SeasonDays),
                DurationDays = SeasonDays,
                ExclusiveCosmeticId = source.ExclusiveCosmeticId
            };
        }

        /// <summary>Index of the season live at this instant (0 = beta).</summary>
        public static int IndexAt(DateTime utc) => At(utc).Index;

        /// <summary>The season with this index, rebuilding the encore one when it is past the authored list.</summary>
        public static SeasonDefinition ByIndex(int index)
        {
            if (index <= 0)
            {
                return All[0];
            }
            if (index < All.Count)
            {
                return All[index];
            }
            int elapsed = index - Last.Index - 1;
            return At(Last.EndUtc.AddDays((double)elapsed * SeasonDays));
        }

        /// <summary>End of a season by index (used to show the remaining days).</summary>
        public static DateTime EndUtc(int index) => ByIndex(index).EndUtc;

        private static DateTime Normalize(DateTime utc) =>
            utc.Kind == DateTimeKind.Utc ? utc : utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        private static List<SeasonDefinition> Build()
        {
            var list = new List<SeasonDefinition>
            {
                // Beta runs until launch day, so testers never see a season number at all.
                Season("beta", 0, Kingdom.Central, BetaStartUtc, (int)(LaunchUtc - BetaStartUtc).TotalDays, "firstlight"),
                Season("s1", 1, Kingdom.North, LaunchUtc, SeasonDays, "frostdawn"),
                Season("s2", 2, Kingdom.East, LaunchUtc.AddDays(SeasonDays), SeasonDays, "sunsteel"),
                Season("s3", 3, Kingdom.West, LaunchUtc.AddDays(2 * SeasonDays), SeasonDays, "thornbloom"),
                Season("s4", 4, Kingdom.South, LaunchUtc.AddDays(3 * SeasonDays), SeasonDays, "emberforge"),
                Season("s5", 5, Kingdom.Central, LaunchUtc.AddDays(4 * SeasonDays), SeasonDays, "prismguard"),
                Season("s6", 6, Kingdom.North, LaunchUtc.AddDays(5 * SeasonDays), SeasonDays, "aurorahunt"),
                Season("s7", 7, Kingdom.East, LaunchUtc.AddDays(6 * SeasonDays), SeasonDays, "mirageveil"),
                Season("s8", 8, Kingdom.West, LaunchUtc.AddDays(7 * SeasonDays), SeasonDays, "rootrune"),
                Season("s9", 9, Kingdom.South, LaunchUtc.AddDays(8 * SeasonDays), SeasonDays, "magmacrown"),
                Season("s10", 10, Kingdom.Central, LaunchUtc.AddDays(9 * SeasonDays), SeasonDays, "shardvault"),
                Season("s11", 11, Kingdom.North, LaunchUtc.AddDays(10 * SeasonDays), SeasonDays, "wintercourt"),
                Season("s12", 12, Kingdom.Central, LaunchUtc.AddDays(11 * SeasonDays), SeasonDays, "sovereign")
            };
            return list;
        }

        private static SeasonDefinition Season(string id, int index, Kingdom theme, DateTime start, int days, string skin) =>
            new SeasonDefinition
            {
                Id = id,
                Index = index,
                NameKey = "season." + id,
                Theme = theme,
                StartUtc = start,
                DurationDays = days,
                ExclusiveCosmeticId = CosmeticPrefix + skin
            };
    }
}
