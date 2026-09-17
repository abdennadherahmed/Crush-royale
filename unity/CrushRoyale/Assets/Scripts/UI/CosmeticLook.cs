using CrushRoyale.Core.Economy;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>How cosmetics look in the UI: rarity colors, avatar frames, title colors, outfit auras, board skins.</summary>
    public static class CosmeticLook
    {
        public static Color Rarity(CosmeticRarity rarity)
        {
            switch (rarity)
            {
                case CosmeticRarity.Rare: return Theme.Hex("5BA8FF");
                case CosmeticRarity.Epic: return Theme.Hex("C77DFF");
                case CosmeticRarity.Legendary: return Theme.Gold;
                default: return Theme.Hex("D8D4E8");
            }
        }

        public static Color Rarity(string id)
        {
            CosmeticDefinition def = CosmeticCatalog.Get(id);
            return def == null ? Theme.TextMuted : Rarity(def.Rarity);
        }

        public static string Name(Localization loc, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }
            if (id.StartsWith(CosmeticCatalog.BattlePassPrefix, System.StringComparison.Ordinal))
            {
                string season = id.Substring(CosmeticCatalog.BattlePassPrefix.Length).Split('.')[0];
                return loc.T(id.EndsWith(".frame", System.StringComparison.Ordinal) ? "profile.bpFrame" : id.EndsWith(".board", System.StringComparison.Ordinal) ? "profile.bpBoard" : "profile.bpPieces", season);
            }
            if (id.StartsWith("frame.page", System.StringComparison.Ordinal))
            {
                return loc.T("profile.pageFrame", id.Substring("frame.page".Length));
            }
            return loc.T("cosmetic." + id);
        }

        /// <summary>Main and accent colors of an avatar frame.</summary>
        public static (Color Main, Color Accent) Frame(string id)
        {
            switch (id)
            {
                case "frame.bronze": return (Theme.Hex("CD7F32"), Theme.Hex("FFD1A1"));
                case "frame.silver": return (Theme.Hex("C0C7D6"), Color.white);
                case "frame.gold": return (Theme.Gold, Theme.Hex("FFF1B8"));
                case "frame.crystal": return (Theme.Crystal, Theme.Hex("E0FAFF"));
                case "frame.royal": return (Theme.Hex("8E44FF"), Theme.Gold);
                case "frame.crown": return (Theme.Gold, Theme.Hex("FF5D6C"));
                case null: case "": return (Theme.GoldDark, Theme.Gold);
            }
            if (id.StartsWith("frame.page", System.StringComparison.Ordinal))
            {
                int.TryParse(id.Substring("frame.page".Length), out int page);
                return (Color.HSVToRGB(page / 12f, 0.6f, 1f), Color.white);
            }
            return (Rarity(id), Color.white);
        }

        /// <summary>Glow behind the hero for an outfit (transparent without one).</summary>
        public static Color OutfitAura(string id)
        {
            switch (id)
            {
                case "outfit.east": return new Color(1f, 0.7f, 0.3f, 0.6f);
                case "outfit.west": return new Color(0.4f, 1f, 0.5f, 0.55f);
                case "outfit.royal": return new Color(1f, 0.85f, 0.35f, 0.7f);
                case "outfit.corrupted": return new Color(0.7f, 0.1f, 0.4f, 0.7f);
                case "outfit.purified": return new Color(0.6f, 0.95f, 1f, 0.7f);
                default: return new Color(0.45f, 0.9f, 1f, 0.35f);
            }
        }

        /// <summary>Frame, cell and aura tints of a board skin.</summary>
        public static (Color Frame, Color CellA, Color CellB) Board(string id)
        {
            switch (id)
            {
                case "board.ice": return (new Color(0.1f, 0.25f, 0.4f, 0.92f), new Color(0.7f, 0.9f, 1f, 0.12f), new Color(0.7f, 0.9f, 1f, 0.05f));
                case "board.lava": return (new Color(0.3f, 0.06f, 0.04f, 0.92f), new Color(1f, 0.5f, 0.2f, 0.1f), new Color(1f, 0.3f, 0.1f, 0.04f));
                case "board.forest": return (new Color(0.06f, 0.2f, 0.1f, 0.92f), new Color(0.5f, 1f, 0.5f, 0.1f), new Color(0.5f, 1f, 0.5f, 0.04f));
                case "board.cavern": return (new Color(0.18f, 0.13f, 0.1f, 0.92f), new Color(1f, 0.85f, 0.6f, 0.08f), new Color(1f, 0.85f, 0.6f, 0.03f));
                case "board.golden": return (new Color(0.3f, 0.22f, 0.05f, 0.92f), new Color(1f, 0.85f, 0.35f, 0.14f), new Color(1f, 0.85f, 0.35f, 0.06f));
                case "board.valdorax": return (new Color(0.12f, 0.02f, 0.18f, 0.94f), new Color(0.8f, 0.3f, 1f, 0.12f), new Color(0.8f, 0.3f, 1f, 0.05f));
                default:
                    if (id != null && id.EndsWith(".board", System.StringComparison.Ordinal))
                    {
                        return (new Color(0.08f, 0.1f, 0.25f, 0.92f), new Color(0.6f, 0.8f, 1f, 0.1f), new Color(0.6f, 0.8f, 1f, 0.04f));
                    }
                    return (new Color(0.08f, 0.06f, 0.16f, 0.92f), new Color(1, 1, 1, 0.06f), new Color(1, 1, 1, 0.03f));
            }
        }

        /// <summary>Round avatar: portrait inside the equipped frame (colored rings, a crown on the crown frame).</summary>
        public static RectTransform Avatar(Transform parent, Sprite portrait, string frameId, float size)
        {
            (Color main, Color accent) = Frame(frameId);
            RectTransform root = UIFactory.Rect("Avatar", parent);
            root.sizeDelta = new Vector2(size, size);
            Image glow = UIFactory.Icon(root, ProceduralSprites.Glow(128), new Color(main.r, main.g, main.b, 0.6f), 0);
            UIFactory.Stretch(glow.rectTransform, -size * 0.25f, -size * 0.25f, -size * 0.25f, -size * 0.25f);
            if (!string.IsNullOrEmpty(frameId))
            {
                glow.gameObject.AddComponent<Pulse>();
            }
            Image outer = UIFactory.Icon(root, ProceduralSprites.Circle(), main, 0);
            UIFactory.Stretch(outer.rectTransform);
            Image inner = UIFactory.Icon(root, ProceduralSprites.Circle(), accent, 0);
            UIFactory.Stretch(inner.rectTransform, size * 0.05f, size * 0.05f, size * 0.05f, size * 0.05f);
            Image back = UIFactory.Icon(root, ProceduralSprites.Circle(), new Color(0.1f, 0.06f, 0.2f, 1f), 0);
            RectTransform backRect = UIFactory.Stretch(back.rectTransform, size * 0.09f, size * 0.09f, size * 0.09f, size * 0.09f);
            backRect.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            if (portrait != null)
            {
                Image face = UIFactory.Icon(backRect, portrait, Color.white, 0);
                UIFactory.Stretch(face.rectTransform, -size * 0.04f, -size * 0.04f, 0f, -size * 0.12f);
            }
            if (frameId == "frame.crown" || frameId == "frame.royal")
            {
                Sprite crown = UiKit.Art("item_crown");
                if (crown != null)
                {
                    Image crownImage = UIFactory.Icon(root, crown, Color.white, 0);
                    UIFactory.Anchor(crownImage.rectTransform, 0.25f, 0.82f, 0.75f, 1.2f);
                }
            }
            return root;
        }
    }
}
