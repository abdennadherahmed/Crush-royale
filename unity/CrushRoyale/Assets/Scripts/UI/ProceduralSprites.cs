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
