using System.Collections.Generic;
using UnityEngine;

namespace CrushRoyale.Game.UI
{
    /// <summary>Visual identity: deep night-violet backgrounds, crystal accents, royal gold.</summary>
    public static class Theme
    {
        public static readonly Color Background = Hex("140F24");
        public static readonly Color BackgroundLight = Hex("211A3A");
        public static readonly Color Panel = Hex("2B2350");
        public static readonly Color PanelLight = Hex("3A3068");
        public static readonly Color Overlay = new Color(0.03f, 0.02f, 0.08f, 0.82f);
        public static readonly Color Gold = Hex("F5C451");
        public static readonly Color GoldDark = Hex("B8862B");
        public static readonly Color Crystal = Hex("6FD6FF");
        public static readonly Color Success = Hex("4CD18A");
        public static readonly Color Danger = Hex("FF5D6C");
        public static readonly Color Warning = Hex("FFA53D");
        public static readonly Color Text = Hex("F4F1FF");
        public static readonly Color TextMuted = Hex("A79FCC");
        public static readonly Color Orbe = Hex("C77DFF");
        public static readonly Color RedSurge = Hex("FF2E4D");

        // --- Depth system -------------------------------------------------------------------------------------
        // Screens are built from translucent "glass" surfaces over the backdrop art instead of opaque purple slabs:
        // the illustration stays visible, and the stack of surfaces reads as layers rather than as flat rectangles.

        /// <summary>Resting glass surface (cards, rows, list items). Translucent on purpose: the backdrop shows through.</summary>
        public static readonly Color Glass = new Color(0.055f, 0.037f, 0.125f, 0.72f);

        /// <summary>One step above the page: dialogs, popups, the hub's own panels.</summary>
        public static readonly Color GlassRaised = new Color(0.078f, 0.055f, 0.165f, 0.88f);

        /// <summary>Recessed well (inputs, tracks, empty slots): darker than the page so it reads as a hole.</summary>
        public static readonly Color GlassSunken = new Color(0.025f, 0.018f, 0.06f, 0.78f);

        /// <summary>Hairline along a surface edge; the single cheapest cue that a rectangle has a physical edge.</summary>
        public static readonly Color Hairline = new Color(0.62f, 0.55f, 0.92f, 0.34f);

        /// <summary>Gold hairline reserved for the surface the player is meant to act on.</summary>
        public static readonly Color HairlineGold = new Color(0.96f, 0.78f, 0.36f, 0.55f);

        /// <summary>Inner top highlight faked with a bright band: light lands on the upper lip of a bevel.</summary>
        public static readonly Color InnerHighlight = new Color(1f, 0.97f, 0.88f, 0.16f);

        /// <summary>Drop shadow tint. Kept violet rather than black so shadows sit inside the palette.</summary>
        public static readonly Color Shadow = new Color(0.02f, 0.008f, 0.05f, 0.55f);

        // --- Button tiers -------------------------------------------------------------------------------------
        // Gold is scarce on purpose: at most one gold button per screen, so the eye always finds the main action.

        /// <summary>Secondary surface: cold stone, clearly the lesser action next to gold.</summary>
        public static readonly Color Stone = Hex("5A5F73");
        public static readonly Color StoneDark = Hex("2A2E3C");

        // --- Typography rhythm --------------------------------------------------------------------------------
        // One scale for the whole game (ratio ~1.3). Anything below MinFontSize is clamped by UIFactory.Label.

        public const int DisplaySize = 84;
        public const int TitleSize = 64;
        public const int HeaderSize = 48;
        public const int SubtitleSize = 40;
        public const int BodySize = 36;
        public const int SmallSize = 28;
        public const int CaptionSize = 24;

        // --- Spacing and shape ---------------------------------------------------------------------------------
        // A 8 px base grid: every gap in the UI is one of these, so unrelated screens end up with the same rhythm.

        public const int SpaceXs = 8;
        public const int SpaceS = 16;
        public const int SpaceM = 24;
        public const int SpaceL = 36;
        public const int SpaceXl = 56;

        /// <summary>Corner radius of a resting surface; buttons use ButtonRadius, chips use ChipRadius.</summary>
        public const int Radius = 28;
        public const int ButtonRadius = 34;
        public const int ChipRadius = 18;

        /// <summary>Gem colors indexed by PieceColor (Red, Blue, Green, Yellow, Purple, Orange).</summary>
        public static readonly Color[] Gems =
        {
            Hex("FF4B5C"), Hex("3D8BFF"), Hex("3DDC84"), Hex("FFF03A"), Hex("B061FF"), Hex("FF6A1A")
        };

        /// <summary>Color-blind palette (Okabe-Ito based) used when the accessibility option is on.</summary>
        public static readonly Color[] GemsColorBlind =
        {
            Hex("D55E00"), Hex("0072B2"), Hex("009E73"), Hex("F0E442"), Hex("CC79A7"), Hex("E69F00")
        };

        public static readonly Dictionary<string, Color> Leagues = new Dictionary<string, Color>
        {
            ["Bronze"] = Hex("CD7F32"),
            ["Silver"] = Hex("C0C7D6"),
            ["Gold"] = Hex("F5C451"),
            ["Platinum"] = Hex("7FE3E0"),
            ["Diamond"] = Hex("9AB8FF"),
            ["Master"] = Hex("FF6FD8")
        };

        public static Color League(string league) => league != null && Leagues.TryGetValue(league, out Color c) ? c : TextMuted;

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            return color;
        }
    }
}
