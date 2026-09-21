using System.Collections.Generic;
using UnityEngine;

namespace CrushRoyale.Game.UI
{
    /// <summary>
    /// Generates every sprite at runtime (rounded panels, circles, gem shapes, icons), so the game is complete without
    /// art assets. Shapes differ per color (circle, diamond, square, triangle, hexagon, star): color-blind friendly.
    /// </summary>
    public static class ProceduralSprites
    {
        public enum GemShape
        {
            Circle,
            Diamond,
            Square,
            Triangle,
            Hexagon,
            Star
        }

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite RoundedRect(int radius = 24)
        {
            string key = "rr" + radius;
            if (Cache.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }
            int size = radius * 2 + 4;
            Texture2D tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Abs(x + 0.5f - size / 2f) - (size / 2f - radius));
                    float dy = Mathf.Max(0, Mathf.Abs(y + 0.5f - size / 2f) - (size / 2f - radius));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Depth levels of the glass design language. Each one is a pre-shaded tile of the same atlas.</summary>
        public enum SurfaceTone
        {
            /// <summary>Resting surface: list rows, cards, chips. Translucent, the backdrop art shows through.</summary>
            Glass,

            /// <summary>One step towards the player: dialogs, popups, the panel that owns the screen's main action.</summary>
            Raised,

            /// <summary>Recessed well: inputs, progress tracks, empty slots. Reads as a hole, not as a tile.</summary>
            Sunken,

            /// <summary>Resting surface with a gold hairline: the one card per screen that carries the main action.</summary>
            Gold
        }

        /// <summary>Pixels of slack around a surface tile: the room the blurred drop shadow needs on every side.</summary>
        public const int SurfacePad = 14;

        /// <summary>
        /// A glass surface: blurred drop shadow, translucent top-lit body, rim hairline and inner top gleam, all baked
        /// into one pre-shaded 9-slice tile. Every tone of a given radius lives in one texture and the Image tint stays
        /// white, so a screen full of cards is a single draw call and costs nothing extra on a mid-range phone.
        /// Use it with Image.type = Sliced; the shadow bleeds <see cref="SurfacePad"/> px inside the element's own rect.
        /// </summary>
        public static Sprite Surface(SurfaceTone tone = SurfaceTone.Glass, int radius = Theme.Radius)
        {
            radius = Mathf.Clamp(radius, 6, 96);
            string key = "surf" + radius + (int)tone;
            if (Cache.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }
            BuildSurfaceAtlas(radius);
            return Cache[key];
        }

        // Per tone: body color at the top edge, body color at the bottom edge, body alpha, rim color, rim alpha.
        private static readonly (Color Top, Color Bottom, float Alpha, Color Rim, float RimAlpha)[] Tones =
        {
            (new Color(0.105f, 0.075f, 0.215f), new Color(0.045f, 0.030f, 0.105f), 0.80f, new Color(0.70f, 0.63f, 1f), 0.40f),
            (new Color(0.145f, 0.105f, 0.275f), new Color(0.062f, 0.042f, 0.140f), 0.93f, new Color(0.78f, 0.70f, 1f), 0.50f),
            (new Color(0.034f, 0.024f, 0.080f), new Color(0.068f, 0.050f, 0.140f), 0.82f, new Color(0.46f, 0.40f, 0.72f), 0.34f),
            (new Color(0.125f, 0.088f, 0.200f), new Color(0.052f, 0.034f, 0.098f), 0.86f, new Color(1f, 0.82f, 0.42f), 0.70f)
        };

        private static void BuildSurfaceAtlas(int radius)
        {
            // 8 px of straight run in the middle: the band the 9-slice stretches, so the corners never smear.
            int side = 2 * (radius + SurfacePad) + 8;
            const int gutter = 4;
            int count = Tones.Length;
            int width = side * count + gutter * (count - 1);
            Texture2D tex = NewTexture(width, side);
            var pixels = new Color32[width * side];

            var mask = new float[side * side];
            var lip = new float[side * side];
            var inner = new float[side * side];
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    mask[y * side + x] = RoundedCoverage(x, y, side, side, radius, 0f);
                    lip[y * side + x] = RoundedCoverage(x, y, side, side, Mathf.Max(1, radius - 2), 2.4f);
                    inner[y * side + x] = RoundedCoverage(x, y, side, side, Mathf.Max(2, radius - SurfacePad), SurfacePad);
                }
            }
            float[] blurred = Blur(inner, side, side, SurfacePad / 2);

            for (int tone = 0; tone < count; tone++)
            {
                (Color top, Color bottom, float alpha, Color rimColor, float rimAlpha) = Tones[tone];
                int originX = tone * (side + gutter);
                // Sunken surfaces are lit from below (the far wall of a hole catches the light) instead of from above.
                bool sunken = tone == (int)SurfaceTone.Sunken;
                for (int y = 0; y < side; y++)
                {
                    for (int x = 0; x < side; x++)
                    {
                        int index = y * side + x;
                        float m = mask[index];
                        float t = (y + 0.5f) / side;

                        // Bottom-up compositing, premultiplied: shadow, then body, then rim, then gleam.
                        float ar = 0f, ag = 0f, ab = 0f, aa = 0f;
                        int shadowY = Mathf.Clamp(y + 5, 0, side - 1);
                        Over(ref ar, ref ag, ref ab, ref aa, 0.02f, 0.008f, 0.05f, Mathf.Clamp01(blurred[shadowY * side + x]) * 0.5f * (1f - m));

                        Color body = Color.Lerp(bottom, top, sunken ? 1f - t : t);
                        Over(ref ar, ref ag, ref ab, ref aa, body.r, body.g, body.b, alpha * m);

                        float rim = Mathf.Clamp01(m - lip[index]) * rimAlpha * (sunken ? 1.1f - 0.5f * t : 0.45f + 0.75f * t);
                        Over(ref ar, ref ag, ref ab, ref aa, rimColor.r, rimColor.g, rimColor.b, Mathf.Clamp01(rim));

                        if (!sunken)
                        {
                            Over(ref ar, ref ag, ref ab, ref aa, 1f, 0.97f, 0.9f, GleamAt(mask, side, x, y, radius) * 0.55f);
                        }

                        float inv = aa > 0.001f ? 1f / aa : 0f;
                        pixels[y * width + originX + x] = new Color32(
                            (byte)(Mathf.Clamp01(ar * inv) * 255),
                            (byte)(Mathf.Clamp01(ag * inv) * 255),
                            (byte)(Mathf.Clamp01(ab * inv) * 255),
                            (byte)(Mathf.Clamp01(aa) * 255));
                    }
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            // Border of the 9-slice: the whole corner plus the shadow slack, so stretching never distorts the rounding.
            float border = radius + SurfacePad;
            var borders = new Vector4(border, border, border, border);
            for (int tone = 0; tone < count; tone++)
            {
                var rect = new Rect(tone * (side + gutter), 0, side, side);
                Cache["surf" + radius + tone] = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, borders);
            }
        }

        /// <summary>Alpha "over" compositing into a premultiplied accumulator.</summary>
        private static void Over(ref float dr, ref float dg, ref float db, ref float da, float r, float g, float b, float a)
        {
            if (a <= 0f)
            {
                return;
            }
            a = Mathf.Clamp01(a);
            float keep = 1f - a;
            dr = r * a + dr * keep;
            dg = g * a + dg * keep;
            db = b * a + db * keep;
            da = a + da * keep;
        }

        /// <summary>Anti-aliased coverage of a rounded rectangle inset by <paramref name="inset"/> pixels.</summary>
        private static float RoundedCoverage(int x, int y, int w, int h, int radius, float inset)
        {
            float halfW = w * 0.5f - inset;
            float halfH = h * 0.5f - inset;
            float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - w * 0.5f) - (halfW - radius));
            float dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - h * 0.5f) - (halfH - radius));
            float outside = Mathf.Max(Mathf.Abs(x + 0.5f - w * 0.5f) - halfW, Mathf.Abs(y + 0.5f - h * 0.5f) - halfH);
            if (outside > 0f)
            {
                return 0f;
            }
            return Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
        }

        /// <summary>Soft band a few pixels under the top edge: the inner highlight that makes a flat panel look bevelled.</summary>
        private static float GleamAt(float[] mask, int side, int x, int y, int radius)
        {
            int depth = side - 1 - y;
            if (depth > radius || mask[y * side + x] < 0.9f)
            {
                return 0f;
            }
            float band = Mathf.Clamp01(1f - Mathf.Abs(depth - 5f) / 9f);
            // Fade out towards the rounded corners so the band does not spill over the silhouette.
            float middle = Mathf.Clamp01(1f - Mathf.Abs(x + 0.5f - side * 0.5f) / (side * 0.5f - radius * 0.6f));
            return band * middle * 0.55f;
        }

        /// <summary>Separable box blur (three passes ~ gaussian) used for the drop shadow tile.</summary>
        private static float[] Blur(float[] source, int w, int h, int radius)
        {
            if (radius < 1)
            {
                return source;
            }
            var a = (float[])source.Clone();
            var b = new float[source.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f;
                        for (int k = -radius; k <= radius; k++)
                        {
                            sum += a[y * w + Mathf.Clamp(x + k, 0, w - 1)];
                        }
                        b[y * w + x] = sum / (radius * 2 + 1);
                    }
                }
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f;
                        for (int k = -radius; k <= radius; k++)
                        {
                            sum += b[Mathf.Clamp(y + k, 0, h - 1) * w + x];
                        }
                        a[y * w + x] = sum / (radius * 2 + 1);
                    }
                }
            }
            return a;
        }

        public static Sprite Circle(int size = 128) => Shape(GemShape.Circle, size, false);

        /// <summary>Gem sprite: white shape with a soft highlight, tinted by the Image color.</summary>
        public static Sprite Gem(GemShape shape, int size = 128) => Shape(shape, size, true);

        /// <summary>Bonus overlays: horizontal/vertical stripes or a ring for area bombs.</summary>
        public static Sprite Stripes(bool horizontal, int size = 128)
        {
            string key = "stripe" + horizontal + size;
            if (Cache.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }
            Texture2D tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int coord = horizontal ? y : x;
                    bool stripe = coord > size * 0.3f && coord < size * 0.7f && ((coord / (size / 10)) % 2 == 0);
                    float edge = horizontal ? Mathf.Min(x, size - x) : Mathf.Min(y, size - y);
                    byte a = stripe && edge > size * 0.18f ? (byte)210 : (byte)0;
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Cache[key] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        public static Sprite Ring(int size = 128)
        {
            string key = "ring" + size;
            if (Cache.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }
            Texture2D tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                    float a = Mathf.Clamp01(1 - Mathf.Abs(d - c * 0.62f) / (c * 0.08f));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Cache[key] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Soft radial glow (bright core, smooth falloff): the base of every particle effect.</summary>
        public static Sprite Glow(int size = 64)
        {
            string key = "glow" + size;
            if (Cache.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }
            Texture2D tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Clamp01(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c)) / c);
                    float a = Mathf.Pow(1f - d, 2.2f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Cache[key] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Vertical alpha ramp: opaque at the top (or bottom) edge, transparent at the other.</summary>
        public static Sprite VerticalFade(bool opaqueTop)
        {
            string key = "fade" + opaqueTop;
            if (Cache.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }
            const int height = 64;
            Texture2D tex = NewTexture(4, height);
            var pixels = new Color32[4 * height];
            for (int y = 0; y < height; y++)
            {
                float t = (y + 0.5f) / height;
                float a = Mathf.SmoothStep(0f, 1f, opaqueTop ? t : 1f - t);
                for (int x = 0; x < 4; x++)
                {
                    pixels[y * 4 + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Cache[key] = Sprite.Create(tex, new Rect(0, 0, 4, height), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>
        /// Radial vignette: transparent in the middle, opaque at the corners. Laid over a screen behind a modal it
        /// fakes the focus falloff a blur would give, for the price of one stretched 64x64 sprite.
        /// </summary>
        public static Sprite Vignette(int size = 64)
        {
            string key = "vignette" + size;
            if (Cache.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }
            Texture2D tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Clamp01(Mathf.Sqrt(nx * nx + ny * ny) / 1.414f);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.28f) / 0.72f));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Cache[key] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Four-point twinkle star with a glowing core.</summary>
        public static Sprite Spark(int size = 64)
        {
            string key = "spark" + size;
            if (Cache.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }
            Texture2D tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs((x + 0.5f) / size * 2 - 1);
                    float ny = Mathf.Abs((y + 0.5f) / size * 2 - 1);
                    float core = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny) * 2.2f), 1.5f);
                    float cross = Mathf.Max(Mathf.Exp(-nx * 22f) * (1f - ny), Mathf.Exp(-ny * 22f) * (1f - nx));
                    float a = Mathf.Clamp01(Mathf.Max(core, cross));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Cache[key] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Cracked rock used for stones.</summary>
        public static Sprite Stone(int size = 128)
        {
            string key = "stone" + size;
            if (Cache.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }
            Texture2D tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            var rng = new System.Random(42);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2 - 1;
                    float ny = (y + 0.5f) / size * 2 - 1;
                    float r = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny)) * 0.6f + Mathf.Sqrt(nx * nx + ny * ny) * 0.4f;
                    float a = Mathf.Clamp01((0.92f - r) * 20);
                    float shade = 0.55f + 0.25f * (1 - ny) * 0.5f + (float)rng.NextDouble() * 0.08f;
                    bool crack = Mathf.Abs(nx * 0.8f - ny + 0.1f * Mathf.Sin(nx * 9)) < 0.03f;
                    byte v = (byte)(Mathf.Clamp01(crack ? shade * 0.45f : shade) * 255);
                    pixels[y * size + x] = new Color32(v, v, (byte)(v * 0.95f), (byte)(a * 255));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Cache[key] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite Shape(GemShape shape, int size, bool shading)
        {
            string key = "shape" + shape + size + shading;
            if (Cache.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }

            Texture2D tex = NewTexture(size, size);
            var pixels = new Color32[size * size];
            const int ss = 3;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        for (int sx = 0; sx < ss; sx++)
                        {
                            float nx = (x + (sx + 0.5f) / ss) / size * 2 - 1;
                            float ny = (y + (sy + 0.5f) / ss) / size * 2 - 1;
                            if (Inside(shape, nx, ny * -1f))
                            {
                                inside++;
                            }
                        }
                    }
                    float alpha = inside / (float)(ss * ss);
                    float light = 1f;
                    if (shading)
                    {
                        float nx = (x + 0.5f) / size * 2 - 1;
                        float ny = (y + 0.5f) / size * 2 - 1;
                        float highlight = Mathf.Clamp01(1 - Vector2.Distance(new Vector2(nx, ny), new Vector2(-0.3f, 0.35f)) * 1.6f);
                        light = 0.78f + 0.22f * (ny + 1) * 0.5f + highlight * 0.35f;
                    }
                    byte v = (byte)(Mathf.Clamp01(light) * 255);
                    pixels[y * size + x] = new Color32(v, v, v, (byte)(alpha * 255));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Cache[key] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static bool Inside(GemShape shape, float x, float y)
        {
            const float r = 0.86f;
            switch (shape)
            {
                case GemShape.Circle:
                    return x * x + y * y <= r * r;
                case GemShape.Diamond:
                    return Mathf.Abs(x) + Mathf.Abs(y) * 0.8f <= r;
                case GemShape.Square:
                    {
                        float q = 0.72f;
                        float corner = 0.18f;
                        float dx = Mathf.Max(0, Mathf.Abs(x) - (q - corner));
                        float dy = Mathf.Max(0, Mathf.Abs(y) - (q - corner));
                        return dx * dx + dy * dy <= corner * corner;
                    }
                case GemShape.Triangle:
                    return y >= -0.72f && y <= 0.86f && Mathf.Abs(x) <= (0.86f - y) * 0.6f;
                case GemShape.Hexagon:
                    {
                        float ax = Mathf.Abs(x);
                        float ay = Mathf.Abs(y);
                        return ay <= r * 0.866f && ax * 0.866f + ay * 0.5f <= r * 0.866f;
                    }
                default:
                    {
                        float angle = Mathf.Atan2(y, x);
                        float radius = Mathf.Sqrt(x * x + y * y);
                        float star = 0.48f + 0.38f * Mathf.Pow(Mathf.Abs(Mathf.Cos(2.5f * angle + Mathf.PI / 2)), 0.9f);
                        return radius <= star;
                    }
            }
        }

        private static Texture2D NewTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            return tex;
        }
    }
}
