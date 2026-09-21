using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>
    /// Illustrated UI kit (Resources/Art/Kit): glossy gold-trimmed buttons, ornate panels, ribbons, card frames and bars.
    /// Sprites are 9-sliced at runtime so any size keeps crisp corners. Missing files fall back to the procedural look.
    /// </summary>
    public static class UiKit
    {
        public enum ButtonStyle
        {
            Gold,
            Purple,
            Green,
            Red,

            /// <summary>Cold silver-stone face, no gold rim: the secondary tier of the emphasis ladder.</summary>
            Stone
        }

        /// <summary>Illustrated family that carries a button tier (Tertiary has no face: it is drawn as glass).</summary>
        public static ButtonStyle StyleOf(UIFactory.ButtonTier tier)
        {
            switch (tier)
            {
                case UIFactory.ButtonTier.Positive: return ButtonStyle.Green;
                case UIFactory.ButtonTier.Destructive: return ButtonStyle.Red;
                case UIFactory.ButtonTier.Secondary: return ButtonStyle.Stone;
                default: return ButtonStyle.Gold;
            }
        }

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> Plain = new Dictionary<string, Sprite>();

        /// <summary>Loads a kit texture as a sliced sprite; border is the share of the smaller side kept unstretched.</summary>
        public static Sprite Sliced(string name, float borderShare = 0.42f)
        {
            string key = name + borderShare;
            if (Cache.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }
            Texture2D texture = Resources.Load<Texture2D>("Art/Kit/" + name);
            Sprite sprite = null;
            if (texture != null)
            {
                float border = Mathf.Floor(Mathf.Min(texture.width, texture.height) * borderShare);
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            }
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Whole illustration (icons, item art, opened chests) from Resources/Art/Kit; null when absent.</summary>
        public static Sprite Art(string name)
        {
            if (Plain.TryGetValue(name, out Sprite cached))
            {
                return cached;
            }
            Sprite sprite = Resources.Load<Sprite>("Art/Kit/" + name);
            if (sprite == null)
            {
                Texture2D texture = Resources.Load<Texture2D>("Art/Kit/" + name);
                if (texture != null)
                {
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                }
            }
            Plain[name] = sprite;
            return sprite;
        }

        public static Sprite Button(ButtonStyle style) => Sliced("button_" + style.ToString().ToLowerInvariant());

        /// <summary>Maps the flat colors used across the screens to the illustrated button family.</summary>
        public static bool TryStyleFor(Color color, out ButtonStyle style)
        {
            style = ButtonStyle.Gold;
            if (color.a < 0.1f)
            {
                return false;
            }
            Color.RGBToHSV(color, out float h, out float s, out float v);
            if (Mathf.Abs(color.r - Theme.Stone.r) < 0.06f && Mathf.Abs(color.g - Theme.Stone.g) < 0.06f && Mathf.Abs(color.b - Theme.Stone.b) < 0.06f)
            {
                // The explicit secondary tone; the other desaturated surfaces keep the violet face they always had.
                style = ButtonStyle.Stone;
                return true;
            }
            if (s < 0.35f || v < 0.35f)
            {
                // Dark panels, grey and violet surfaces: secondary actions.
                style = ButtonStyle.Purple;
                return true;
            }
            float hue = h * 360f;
            if (hue < 18f || hue >= 330f)
            {
                style = ButtonStyle.Red;
            }
            else if (hue < 70f)
            {
                style = ButtonStyle.Gold;
            }
            else if (hue < 175f)
            {
                style = ButtonStyle.Green;
            }
            else
            {
                style = ButtonStyle.Purple;
            }
            return true;
        }

        /// <summary>Applies an illustrated sliced sprite to an image, keeping the borders proportional to its height.</summary>
        public static bool Apply(Image image, Sprite sprite, float maxBorderPixels = 60f)
        {
            if (image == null || sprite == null)
            {
                return false;
            }
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            KitSlice slice = image.GetComponent<KitSlice>();
            if (slice == null)
            {
                slice = image.gameObject.AddComponent<KitSlice>();
            }
            slice.MaxBorderPixels = maxBorderPixels;
            slice.Refresh();
            return true;
        }

        /// <summary>Changes a button's look: kit buttons switch family (gold, purple, green, red), flat ones get the color.</summary>
        public static void Recolor(Image image, Color color)
        {
            if (image == null)
            {
                return;
            }
            if (image.GetComponent<KitSlice>() != null && TryStyleFor(color, out ButtonStyle style) && Apply(image, Button(style)))
            {
                return;
            }
            image.color = color;
        }

        /// <summary>Ornate window frame (popups, dialogs). Returns false when the kit art is absent.</summary>
        public static bool FramePanel(Image image) => Apply(image, Sliced("panel_ornate", 0.3f), 64f);

        /// <summary>Gold-trimmed card frame for list rows.</summary>
        public static bool CardFrame(Image image) => Apply(image, Sliced("card_frame", 0.3f), 30f);

        /// <summary>
        /// Glass card: translucent dark pane, violet bevel, slim gold hairline. The default surface of the game -
        /// unlike CardFrame it lets the backdrop illustration show through, which is what stops a page of rows from
        /// reading as a stack of opaque slabs. Falls back to the procedural glass tile when the art is absent.
        /// </summary>
        public static bool GlassCard(Image image) => Apply(image, Sliced("glass_card", 0.32f), 34f);

        /// <summary>
        /// Wide title plate with gold rails and crystal studs: the header of a full-width title bar. The border share
        /// is large on purpose - the studs sit ~50 px in from the ends and must stay inside the unstretched corners.
        /// </summary>
        public static bool Banner(Image image) => Apply(image, Sliced("header_banner", 0.44f), 52f);

        /// <summary>Square gold-rimmed pad for single-glyph buttons (back arrow, cog, close).</summary>
        public static bool IconButton(Image image) => Apply(image, Sliced("icon_button", 0.38f), 30f);

        /// <summary>
        /// Tab plate. The active tab is the illustrated gold one; an inactive tab is deliberately NOT an illustration
        /// but a sunken glass well, so the strip reads as one raised tab among holes instead of a row of rectangles.
        /// </summary>
        public static bool Tab(Image image, bool active)
        {
            if (active && Apply(image, Sliced("tab_active", 0.3f), 26f))
            {
                return true;
            }
            image.sprite = ProceduralSprites.Surface(active ? ProceduralSprites.SurfaceTone.Gold : ProceduralSprites.SurfaceTone.Sunken, Theme.ChipRadius);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            KitSlice slice = image.GetComponent<KitSlice>();
            if (slice != null)
            {
                // The procedural tile is authored at final scale: no border rescaling, unlike the illustrated pieces.
                UnityEngine.Object.Destroy(slice);
            }
            return true;
        }

        /// <summary>
        /// Gold rail with a crystal at its centre: separates blocks inside a page without adding another box.
        /// The rail and the crystal are two images on purpose - a 9-sliced strip stretches its middle band, so an
        /// ornament baked into the centre of the sprite would smear as soon as the divider got wider.
        /// </summary>
        public static Image Divider(Transform parent, float height = 28f)
        {
            Texture2D texture = Resources.Load<Texture2D>("Art/Kit/divider");
            if (texture == null)
            {
                return null;
            }
            if (!Cache.TryGetValue("divider-h", out Sprite sprite))
            {
                // Sliced on the horizontal only: the ends keep their taper and the plain rail in between stretches.
                float side = Mathf.Floor(texture.width * 0.2f);
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(side, 0, side, 0));
                Cache["divider-h"] = sprite;
            }
            Image image = UIFactory.Icon(parent, sprite, Color.white, 0);
            image.preserveAspect = false;
            image.type = Image.Type.Sliced;
            image.gameObject.AddComponent<KitSlice>().MatchHeight = true;
            UIFactory.Height(image, height);

            Sprite crystal = Art("round_crystal");
            if (crystal != null)
            {
                Image jewel = UIFactory.Icon(image.transform, crystal, Color.white, 0);
                jewel.rectTransform.anchorMin = jewel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                jewel.rectTransform.anchoredPosition = Vector2.zero;
                jewel.rectTransform.sizeDelta = new Vector2(height * 1.5f, height * 1.5f);
            }
            return image;
        }

        /// <summary>Purple ribbon behind a title (stretched to its parent; only the middle band stretches).</summary>
        public static Image Ribbon(RectTransform parent)
        {
            Texture2D texture = Resources.Load<Texture2D>("Art/Kit/ribbon");
            if (texture == null)
            {
                return null;
            }
            if (!Cache.TryGetValue("ribbon-h", out Sprite sprite))
            {
                float side = Mathf.Floor(texture.width * 0.3f);
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(side, 0, side, 0));
                Cache["ribbon-h"] = sprite;
            }
            Image image = UIFactory.Icon(parent, sprite, Color.white, 0);
            UIFactory.Stretch(image.rectTransform);
            image.preserveAspect = false;
            image.type = Image.Type.Sliced;
            image.gameObject.AddComponent<KitSlice>().MatchHeight = true;
            image.transform.SetAsFirstSibling();
            return image;
        }

        /// <summary>Framed track with a glossy colored fill; returns the fill image (use UIFactory.SetProgress).</summary>
        public static Image Bar(Transform parent, float value, Color fill, out RectTransform root)
        {
            RectTransform rect = UIFactory.Rect("Progress", parent);
            root = rect;
            Image track = rect.gameObject.AddComponent<Image>();
            if (!Apply(track, Sliced("bar_track", 0.45f), 22f))
            {
                track.sprite = ProceduralSprites.RoundedRect(28);
                track.type = Image.Type.Sliced;
                track.color = Theme.BackgroundLight;
            }
            Image bar = UIFactory.Panel("Fill", rect, fill);
            bar.raycastTarget = false;
            bar.rectTransform.anchorMin = Vector2.zero;
            bar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
            bar.rectTransform.offsetMin = new Vector2(9, 8);
            bar.rectTransform.offsetMax = new Vector2(-9, -8);
            Image shine = UIFactory.Panel("Shine", bar.transform, new Color(1f, 1f, 1f, 0.3f));
            shine.raycastTarget = false;
            UIFactory.Anchor(shine.rectTransform, 0.01f, 0.56f, 0.99f, 0.9f);
            return bar;
        }

        /// <summary>Round badge (gold coin frame or crystal) holding an icon, e.g. hub side buttons.</summary>
        public static Image RoundBadge(Transform parent, bool crystal)
        {
            Sprite sprite = Art(crystal ? "round_crystal" : "round_gold");
            if (sprite == null)
            {
                return null;
            }
            Image image = UIFactory.Icon(parent, sprite, Color.white, 0);
            UIFactory.Stretch(image.rectTransform);
            image.transform.SetAsFirstSibling();
            return image;
        }
    }

    /// <summary>Keeps 9-slice borders proportional to the element height so small and large widgets look alike.</summary>
    public sealed class KitSlice : MonoBehaviour
    {
        /// <summary>The rendered border never exceeds this many canvas pixels (large panels keep slim frames).</summary>
        public float MaxBorderPixels = 60f;

        /// <summary>Scale the sprite to the element height (horizontal-only slicing: ribbons).</summary>
        public bool MatchHeight;

        private Image _image;

        private void OnEnable() => Refresh();

        private void OnRectTransformDimensionsChange() => Refresh();

        public void Refresh()
        {
            if (_image == null)
            {
                _image = GetComponent<Image>();
            }
            if (_image == null || _image.sprite == null)
            {
                return;
            }
            float height = ((RectTransform)transform).rect.height;
            if (height <= 1f)
            {
                return;
            }
            if (MatchHeight)
            {
                _image.pixelsPerUnitMultiplier = Mathf.Max(0.05f, _image.sprite.rect.height / height);
                return;
            }
            float border = Mathf.Max(_image.sprite.border.y, _image.sprite.border.w);
            // Borders take ~40% of the height at most (and MaxBorderPixels), whatever the sprite resolution.
            float rendered = Mathf.Min(height * 0.4f, MaxBorderPixels);
            _image.pixelsPerUnitMultiplier = Mathf.Clamp(border / Mathf.Max(1f, rendered), 0.2f, 40f);
        }
    }
}
