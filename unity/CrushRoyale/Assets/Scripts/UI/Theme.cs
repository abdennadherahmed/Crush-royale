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

        public const int TitleSize = 64;
        public const int HeaderSize = 48;
        public const int BodySize = 36;
        public const int SmallSize = 28;

        /// <summary>Gem colors indexed by PieceColor (Red, Blue, Green, Yellow, Purple, Orange).</summary>
        public static readonly Color[] Gems =
        {
            Hex("FF4B5C"), Hex("3D8BFF"), Hex("3DDC84"), Hex("FFD23F"), Hex("B061FF"), Hex("FF8C2B")
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
