using CrushRoyale.Core.Story;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>
    /// Cheap looping decorations for the Crystalheim restoration scene: water glints, waving banners, torch flames,
    /// chimney smoke and a radiance halo. Everything is built from ProceduralSprites (no extra textures, no particle
    /// system) and every component settles into a still, readable pose when the player asked for reduced motion.
    /// </summary>
    public static class KingdomFx
    {
        public static bool ReduceMotion => GameRoot.Instance != null && GameRoot.Instance.Save.Settings.ReduceMotion;

        /// <summary>Tint of a decoration, so each effect reads instantly even before it moves.</summary>
        public static Color Tint(RestorationEffect effect)
        {
            switch (effect)
            {
                case RestorationEffect.Water: return new Color(0.55f, 0.85f, 1f, 1f);
                case RestorationEffect.Banner: return new Color(1f, 0.35f, 0.45f, 1f);
                case RestorationEffect.Torch: return new Color(1f, 0.68f, 0.25f, 1f);
                case RestorationEffect.Smoke: return new Color(0.82f, 0.82f, 0.9f, 1f);
                case RestorationEffect.Radiance: return new Color(1f, 0.9f, 0.45f, 1f);
                default: return Color.white;
            }
        }

        /// <summary>
        /// Spawns the decoration of a built task at a normalized point of the scene. Size is in scene units (the
        /// painting is laid out at roughly 1000 x 560).
        /// </summary>
        public static void Spawn(RectTransform scene, RestorationEffect effect, Vector2 point, float size)
        {
            switch (effect)
            {
                case RestorationEffect.Water:
                    Water(scene, point, size);
                    break;
                case RestorationEffect.Banner:
                    Banners(scene, point, size);
                    break;
                case RestorationEffect.Torch:
                    Torches(scene, point, size);
                    break;
                case RestorationEffect.Smoke:
                    Smoke(scene, point, size);
                    break;
                case RestorationEffect.Radiance:
                    Radiance(scene, point, size);
                    break;
            }
        }

        private static RectTransform At(RectTransform parent, string name, Vector2 point, float width, float height)
        {
            RectTransform rect = UIFactory.Rect(name, parent);
            rect.anchorMin = rect.anchorMax = point;
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private static Image Fill(RectTransform parent, Sprite sprite, Color color)
        {
            Image image = UIFactory.Icon(parent, sprite, color, 0);
            image.raycastTarget = false;
            UIFactory.Stretch(image.rectTransform);
            return image;
        }

        /// <summary>Three glints sliding across the water surface, plus a permanent blue sheen.</summary>
        private static void Water(RectTransform scene, Vector2 point, float size)
        {
            RectTransform root = At(scene, "Water", point, size * 1.6f, size * 0.55f);
            Image sheen = Fill(root, ProceduralSprites.Glow(64), new Color(0.5f, 0.85f, 1f, 0.28f));
            sheen.rectTransform.localScale = new Vector3(1.4f, 0.6f, 1f);
            for (int i = 0; i < 3; i++)
            {
                RectTransform glint = At(root, "Glint" + i, new Vector2(0.5f, 0.5f), size * 0.5f, size * 0.09f);
                Fill(glint, ProceduralSprites.Glow(64), new Color(0.9f, 1f, 1f, 0.6f));
                Slide slide = glint.gameObject.AddComponent<Slide>();
                slide.Distance = size * 0.5f;
                slide.Speed = 0.5f + i * 0.17f;
                slide.Phase = i * 2.1f;
                slide.Vertical = size * 0.12f;
            }
        }

        /// <summary>Two pennants pinned at their top edge, swinging around the pole.</summary>
        private static void Banners(RectTransform scene, Vector2 point, float size)
        {
            for (int i = 0; i < 2; i++)
            {
                RectTransform banner = At(scene, "Banner" + i, point, size * 0.22f, size * 0.62f);
                banner.anchoredPosition = new Vector2((i - 0.5f) * size * 0.9f, -size * 0.3f);
                banner.pivot = new Vector2(0.5f, 1f);
                Fill(banner, ProceduralSprites.RoundedRect(8), i == 0 ? new Color(0.85f, 0.2f, 0.3f, 0.95f) : new Color(0.2f, 0.4f, 0.9f, 0.95f));
                RectTransform trim = At(banner, "Trim", new Vector2(0.5f, 0.12f), size * 0.18f, size * 0.07f);
                Fill(trim, ProceduralSprites.Circle(), Theme.Gold);
                Wave wave = banner.gameObject.AddComponent<Wave>();
                wave.Degrees = 5f;
                wave.Speed = 1.5f + i * 0.35f;
                wave.Phase = i * 1.7f;
            }
        }

        /// <summary>A pair of flames: a warm halo that breathes plus a flickering core.</summary>
        private static void Torches(RectTransform scene, Vector2 point, float size)
        {
            for (int i = 0; i < 2; i++)
            {
                RectTransform torch = At(scene, "Torch" + i, point, size * 0.5f, size * 0.5f);
                torch.anchoredPosition = new Vector2((i - 0.5f) * size * 1.1f, 0f);
                Image halo = Fill(torch, ProceduralSprites.Glow(64), new Color(1f, 0.6f, 0.2f, 0.45f));
                halo.rectTransform.localScale = Vector3.one * 1.6f;
                RectTransform core = At(torch, "Flame", new Vector2(0.5f, 0.5f), size * 0.16f, size * 0.26f);
                Fill(core, ProceduralSprites.Glow(64), new Color(1f, 0.93f, 0.6f, 0.95f));
                Flicker flicker = torch.gameObject.AddComponent<Flicker>();
                flicker.Seed = i * 3.3f;
            }
        }

        /// <summary>Puffs that rise, spread and fade, then loop: the chimneys of a lived-in city.</summary>
        private static void Smoke(RectTransform scene, Vector2 point, float size)
        {
            RectTransform root = At(scene, "Smoke", point, size, size * 1.6f);
            for (int i = 0; i < 4; i++)
            {
                RectTransform puff = At(root, "Puff" + i, new Vector2(0.5f, 0f), size * 0.34f, size * 0.34f);
                Fill(puff, ProceduralSprites.Glow(64), new Color(0.85f, 0.85f, 0.92f, 0.4f));
                Rise rise = puff.gameObject.AddComponent<Rise>();
                rise.Height = size * 1.3f;
                rise.Drift = size * 0.35f;
                rise.Period = 3.2f;
                rise.Phase = i * 0.8f;
            }
        }

        /// <summary>The zone crown: a slowly turning ray halo and sparks orbiting it.</summary>
        private static void Radiance(RectTransform scene, Vector2 point, float size)
        {
            RectTransform root = At(scene, "Radiance", point, size * 1.8f, size * 1.8f);
            Image halo = Fill(root, ProceduralSprites.Glow(128), new Color(1f, 0.9f, 0.5f, 0.35f));
            halo.rectTransform.localScale = Vector3.one * 1.2f;
            RectTransform rays = At(root, "Rays", new Vector2(0.5f, 0.5f), size * 1.5f, size * 1.5f);
            Fill(rays, ProceduralSprites.Ring(128), new Color(1f, 0.95f, 0.7f, 0.3f));
            rays.gameObject.AddComponent<Spin>().DegreesPerSecond = 12f;
            for (int i = 0; i < 5; i++)
            {
                RectTransform spark = At(root, "Spark" + i, new Vector2(0.5f, 0.5f), size * 0.13f, size * 0.13f);
                Fill(spark, ProceduralSprites.Spark(), i % 2 == 0 ? Theme.Gold : Color.white);
                Orbit orbit = spark.gameObject.AddComponent<Orbit>();
                orbit.Radius = size * 0.6f;
                orbit.Speed = 0.6f + i * 0.08f;
                orbit.Phase = i * 1.25f;
            }
        }

        // ------------------------------------------------------------------ components

        /// <summary>Back-and-forth slide along a line (water glints). Reduced motion leaves it at the middle.</summary>
        public sealed class Slide : MonoBehaviour
        {
            public float Distance = 100f;
            public float Vertical;
            public float Speed = 0.6f;
            public float Phase;

            private void Start()
            {
                if (ReduceMotion)
                {
                    enabled = false;
                }
            }

            private void Update()
            {
                float k = Mathf.Sin(Time.unscaledTime * Speed + Phase);
                ((RectTransform)transform).anchoredPosition = new Vector2(k * Distance, Mathf.Cos(Time.unscaledTime * Speed * 1.3f + Phase) * Vertical);
                Image image = GetComponentInChildren<Image>();
                if (image != null)
                {
                    Color c = image.color;
                    image.color = new Color(c.r, c.g, c.b, 0.25f + 0.45f * (1f - Mathf.Abs(k)));
                }
            }
        }

        /// <summary>Swing around the top pivot (banners). Reduced motion leaves the cloth hanging straight.</summary>
        public sealed class Wave : MonoBehaviour
        {
            public float Degrees = 6f;
            public float Speed = 1.6f;
            public float Phase;

            private void Start()
            {
                if (ReduceMotion)
                {
                    enabled = false;
                }
            }

            private void Update()
            {
                float k = Mathf.Sin(Time.unscaledTime * Speed + Phase);
                transform.localRotation = Quaternion.Euler(0f, 0f, k * Degrees);
                transform.localScale = new Vector3(1f - Mathf.Abs(k) * 0.08f, 1f, 1f);
            }
        }

        /// <summary>Two-frequency scale and alpha noise (torch flames). Reduced motion leaves a steady lit flame.</summary>
        public sealed class Flicker : MonoBehaviour
        {
            public float Seed;

            private Image[] _images;

            private void Start()
            {
                _images = GetComponentsInChildren<Image>();
                if (ReduceMotion)
                {
                    enabled = false;
                }
            }

            private void Update()
            {
                float t = Time.unscaledTime;
                float k = 0.5f + 0.5f * (Mathf.Sin(t * 9.1f + Seed) * 0.6f + Mathf.Sin(t * 3.7f + Seed * 2f) * 0.4f);
                transform.localScale = new Vector3(1f + k * 0.07f, 1f + k * 0.16f, 1f);
                for (int i = 0; _images != null && i < _images.Length; i++)
                {
                    Color c = _images[i].color;
                    _images[i].color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.35f, 0.95f, i == 0 ? k * 0.6f : k));
                }
            }
        }

        /// <summary>Looping rise + spread + fade (smoke puffs). Reduced motion hides the puffs entirely.</summary>
        public sealed class Rise : MonoBehaviour
        {
            public float Height = 100f;
            public float Drift = 30f;
            public float Period = 3f;
            public float Phase;

            private Image _image;

            private void Start()
            {
                _image = GetComponentInChildren<Image>();
                if (ReduceMotion)
                {
                    gameObject.SetActive(false);
                }
            }

            private void Update()
            {
                float k = Mathf.Repeat(Time.unscaledTime / Mathf.Max(0.1f, Period) + Phase, 1f);
                ((RectTransform)transform).anchoredPosition = new Vector2(Mathf.Sin(k * 3.1f + Phase) * Drift * k, k * Height);
                transform.localScale = Vector3.one * (0.4f + k * 1.1f);
                if (_image != null)
                {
                    Color c = _image.color;
                    _image.color = new Color(c.r, c.g, c.b, 0.42f * Mathf.Clamp01(1f - k) * Mathf.Clamp01(k * 4f));
                }
            }
        }

        /// <summary>Constant rotation (ray halo). Reduced motion keeps the halo still but visible.</summary>
        public sealed class Spin : MonoBehaviour
        {
            public float DegreesPerSecond = 20f;

            private void Start()
            {
                if (ReduceMotion)
                {
                    enabled = false;
                }
            }

            private void Update() => transform.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * DegreesPerSecond);
        }

        /// <summary>Circular orbit (sparks). Reduced motion parks the spark on its circle.</summary>
        public sealed class Orbit : MonoBehaviour
        {
            public float Radius = 40f;
            public float Speed = 0.7f;
            public float Phase;

            private void Start()
            {
                Place(Phase);
                if (ReduceMotion)
                {
                    enabled = false;
                }
            }

            private void Update() => Place(Time.unscaledTime * Speed + Phase);

            private void Place(float angle) =>
                ((RectTransform)transform).anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.55f) * Radius;
        }

        /// <summary>
        /// Slow drifting dust over the ruins. Kept as a component so the screen can simply not add it under reduced
        /// motion, and so each mote keeps its own phase without a coroutine.
        /// </summary>
        public sealed class Dust : MonoBehaviour
        {
            public float Amplitude = 18f;
            public float Speed = 0.4f;

            private Vector2 _origin;
            private float _phase;
            private Image _image;

            private void Start()
            {
                _origin = ((RectTransform)transform).anchoredPosition;
                _phase = Random.value * 6.28f;
                _image = GetComponent<Image>();
                if (ReduceMotion)
                {
                    gameObject.SetActive(false);
                }
            }

            private void Update()
            {
                float t = Time.unscaledTime * Speed + _phase;
                ((RectTransform)transform).anchoredPosition = _origin + new Vector2(Mathf.Sin(t) * Amplitude, Mathf.Cos(t * 0.7f) * Amplitude * 1.6f);
                if (_image != null)
                {
                    Color c = _image.color;
                    _image.color = new Color(c.r, c.g, c.b, 0.25f + 0.45f * (0.5f + 0.5f * Mathf.Sin(t * 1.7f)));
                }
            }
        }
    }
}
