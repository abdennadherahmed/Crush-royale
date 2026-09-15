using System.Collections.Generic;

namespace CrushRoyale.Core.Economy
{
    public enum CosmeticKind : byte
    {
        AvatarFrame = 0,
        BoardSkin = 1,
        PieceSkin = 2,
        Title = 3,
        HeroOutfit = 4,
        Emote = 5
    }

    public enum CosmeticRarity : byte
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3
    }

    public enum CosmeticSource : byte
    {
        Default = 0,
        Shop = 1,
        Vip = 2,
        Achievement = 3,
        BattlePass = 4,
        Story = 5
    }

    public sealed class CosmeticDefinition
    {
        public string Id { get; set; }

        public CosmeticKind Kind { get; set; }

        public CosmeticRarity Rarity { get; set; }

        public CosmeticSource Source { get; set; }

        /// <summary>Shop price (0 = cannot be bought).</summary>
        public int PriceOrbes { get; set; }

        /// <summary>VIP level required to buy or receive it (0 = none).</summary>
        public int RequiredVip { get; set; }
    }

    /// <summary>All cosmetics. Everything is cosmetic only: no gameplay effect (fair monetization pillar).</summary>
    public static class CosmeticCatalog
    {
        public const string BattlePassPrefix = "bp.s";

        private static readonly Dictionary<string, CosmeticDefinition> ById = new Dictionary<string, CosmeticDefinition>();

        public static readonly IReadOnlyList<CosmeticDefinition> All = Build();

        public static CosmeticDefinition Get(string id)
        {
            if (id == null)
            {
                return null;
            }
            if (ById.TryGetValue(id, out CosmeticDefinition def))
            {
                return def;
            }
            // Seasonal battle pass cosmetics: bp.s{season}.frame / bp.s{season}.board / bp.s{season}.pieces
            if (id.StartsWith(BattlePassPrefix, System.StringComparison.Ordinal))
            {
                CosmeticKind kind = id.EndsWith(".frame", System.StringComparison.Ordinal) ? CosmeticKind.AvatarFrame
                    : id.EndsWith(".board", System.StringComparison.Ordinal) ? CosmeticKind.BoardSkin
                    : CosmeticKind.PieceSkin;
                return new CosmeticDefinition { Id = id, Kind = kind, Rarity = CosmeticRarity.Epic, Source = CosmeticSource.BattlePass };
            }
            return null;
        }

        /// <summary>Cosmetics granted automatically when reaching a VIP level.</summary>
        public static List<string> VipRewards(int vipLevel)
        {
            var list = new List<string>();
            foreach (CosmeticDefinition c in All)
            {
                if (c.Source == CosmeticSource.Vip && c.RequiredVip == vipLevel)
                {
                    list.Add(c.Id);
                }
            }
            return list;
        }

        public static string PageFrame(int page) => "frame.page" + page;

        private static List<CosmeticDefinition> Build()
        {
            var list = new List<CosmeticDefinition>
            {
                Def("board.classic", CosmeticKind.BoardSkin, CosmeticRarity.Common, CosmeticSource.Default),
                Def("pieces.classic", CosmeticKind.PieceSkin, CosmeticRarity.Common, CosmeticSource.Default),
                Def("emote.gg", CosmeticKind.Emote, CosmeticRarity.Common, CosmeticSource.Default),

                Def("frame.bronze", CosmeticKind.AvatarFrame, CosmeticRarity.Common, CosmeticSource.Shop, 50),
                Def("frame.silver", CosmeticKind.AvatarFrame, CosmeticRarity.Rare, CosmeticSource.Shop, 120),
                Def("frame.gold", CosmeticKind.AvatarFrame, CosmeticRarity.Epic, CosmeticSource.Shop, 300),
                Def("board.ice", CosmeticKind.BoardSkin, CosmeticRarity.Rare, CosmeticSource.Shop, 200),
                Def("board.lava", CosmeticKind.BoardSkin, CosmeticRarity.Rare, CosmeticSource.Shop, 200),
                Def("board.forest", CosmeticKind.BoardSkin, CosmeticRarity.Rare, CosmeticSource.Shop, 200),
                Def("board.cavern", CosmeticKind.BoardSkin, CosmeticRarity.Rare, CosmeticSource.Shop, 200),
                Def("pieces.gems", CosmeticKind.PieceSkin, CosmeticRarity.Epic, CosmeticSource.Shop, 250),
                Def("outfit.east", CosmeticKind.HeroOutfit, CosmeticRarity.Epic, CosmeticSource.Shop, 300),
                Def("outfit.west", CosmeticKind.HeroOutfit, CosmeticRarity.Epic, CosmeticSource.Shop, 300),
                Def("emote.wow", CosmeticKind.Emote, CosmeticRarity.Common, CosmeticSource.Shop, 30),
                Def("emote.fire", CosmeticKind.Emote, CosmeticRarity.Common, CosmeticSource.Shop, 30),
                Def("emote.crown", CosmeticKind.Emote, CosmeticRarity.Rare, CosmeticSource.Shop, 60),

                Def("frame.crystal", CosmeticKind.AvatarFrame, CosmeticRarity.Rare, CosmeticSource.Vip, 0, 3),
                Def("pieces.runes", CosmeticKind.PieceSkin, CosmeticRarity.Rare, CosmeticSource.Vip, 0, 3),
                Def("frame.royal", CosmeticKind.AvatarFrame, CosmeticRarity.Epic, CosmeticSource.Vip, 0, 5),
                Def("board.golden", CosmeticKind.BoardSkin, CosmeticRarity.Epic, CosmeticSource.Vip, 0, 5),
                Def("outfit.royal", CosmeticKind.HeroOutfit, CosmeticRarity.Epic, CosmeticSource.Vip, 0, 5),
                Def("title.patron", CosmeticKind.Title, CosmeticRarity.Epic, CosmeticSource.Vip, 0, 6),
                Def("frame.crown", CosmeticKind.AvatarFrame, CosmeticRarity.Legendary, CosmeticSource.Vip, 0, 10),

                Def("title.cascade_master", CosmeticKind.Title, CosmeticRarity.Epic, CosmeticSource.Achievement),
                Def("title.dragon_slayer", CosmeticKind.Title, CosmeticRarity.Epic, CosmeticSource.Achievement),
                Def("title.legend", CosmeticKind.Title, CosmeticRarity.Legendary, CosmeticSource.Achievement),
                Def("board.valdorax", CosmeticKind.BoardSkin, CosmeticRarity.Legendary, CosmeticSource.Story),
                Def("outfit.corrupted", CosmeticKind.HeroOutfit, CosmeticRarity.Legendary, CosmeticSource.Story),
                Def("outfit.purified", CosmeticKind.HeroOutfit, CosmeticRarity.Legendary, CosmeticSource.Story)
            };

            for (int page = 1; page <= 12; page++)
            {
                list.Add(Def(PageFrame(page), CosmeticKind.AvatarFrame, page >= 10 ? CosmeticRarity.Legendary : page >= 6 ? CosmeticRarity.Epic : CosmeticRarity.Rare, CosmeticSource.Achievement));
            }

            foreach (CosmeticDefinition c in list)
            {
                ById[c.Id] = c;
            }
            return list;
        }

        private static CosmeticDefinition Def(string id, CosmeticKind kind, CosmeticRarity rarity, CosmeticSource source, int priceOrbes = 0, int requiredVip = 0) =>
            new CosmeticDefinition { Id = id, Kind = kind, Rarity = rarity, Source = source, PriceOrbes = priceOrbes, RequiredVip = requiredVip };
    }
}
