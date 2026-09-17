using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>
    /// Brings the illustrated world map to life: drifting clouds, shimmering waves around the island, smoke from the
    /// volcanoes, a pulsing glow on the crystal citadel and the lava, twinkles on the ice castle.
    /// Positions are normalized on the world map art.
    /// </summary>
    public sealed class MapAnimator : MonoBehaviour
    {
        private struct Puff
        {
            public Image Image;
            public Vector2 Start;
            public Vector2 Velocity;
            public float Age;
            public float Life;
            public float Size;
            public Color Color;
        }

        private static readonly Vector2 Volcano = new Vector2(0.43f, 0.3f);
        private static readonly Vector2 Volcano2 = new Vector2(0.5f, 0.36f);
        private static readonly Vector2 Citadel = new Vector2(0.5f, 0.52f);
        private static readonly Vector2 Lava = new Vector2(0.55f, 0.2f);
        private static readonly Vector2 IceCastle = new Vector2(0.6f, 0.82f);

        private readonly List<Image> _clouds = new List<Image>();
        private readonly List<float> _cloudSpeed = new List<float>();
        private readonly List<Image> _waves = new List<Image>();
        private readonly List<Puff> _puffs = new List<Puff>();
        private readonly List<Image> _twinkles = new List<Image>();
        private RectTransform _rect;
        private Image _citadelGlow;
        private Image _lavaGlow;
        private float _nextPuff;

        public static MapAnimator Create(RectTransform map)
        {
            RectTransform rect = UIFactory.Stretch(UIFactory.Rect("MapLife", map));
            MapAnimator animator = rect.gameObject.AddComponent<MapAnimator>();
            animator._rect = rect;
            animator.Build();
            return animator;
        }

        private Image Spot(Sprite sprite, Vector2 at, Vector2 size, Color color)
        {
            Image image = UIFactory.Icon(_rect, sprite, color, 0);
            image.raycastTarget = false;
            image.preserveAspect = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = at;
            image.rectTransform.sizeDelta = size;
            return image;
        }

        private void Build()
        {
            // Waves: soft light bands along the shore that slide back and forth.
            Vector2[] shore = { new Vector2(0.15f, 0.2f), new Vector2(0.85f, 0.25f), new Vector2(0.12f, 0.75f), new Vector2(0.88f, 0.8f), new Vector2(0.5f, 0.06f) };
            foreach (Vector2 at in shore)
            {
                _waves.Add(Spot(ProceduralSprites.Glow(64), at, new Vector2(160, 26), new Color(0.75f, 0.6f, 1f, 0.25f)));
            }

            _citadelGlow = Spot(ProceduralSprites.Glow(128), Citadel, new Vector2(170, 170), new Color(0.6f, 0.9f, 1f, 0.4f));
            _lavaGlow = Spot(ProceduralSprites.Glow(128), Lava, new Vector2(260, 110), new Color(1f, 0.45f, 0.1f, 0.35f));

            for (int i = 0; i < 6; i++)
            {
                Vector2 at = IceCastle + new Vector2(Random.Range(-0.08f, 0.08f), Random.Range(-0.06f, 0.08f));
                _twinkles.Add(Spot(ProceduralSprites.Spark(), at, new Vector2(26, 26), Color.white));
            }
            for (int i = 0; i < 5; i++)
            {
                Vector2 at = Citadel + new Vector2(Random.Range(-0.06f, 0.06f), Random.Range(-0.05f, 0.1f));
                _twinkles.Add(Spot(ProceduralSprites.Spark(), at, new Vector2(22, 22), new Color(0.8f, 0.6f, 1f)));
            }

            for (int i = 0; i < 4; i++)
            {
                Image cloud = Spot(ProceduralSprites.Glow(128), new Vector2(Random.value, Random.Range(0.55f, 0.95f)), new Vector2(Random.Range(220f, 340f), Random.Range(70f, 110f)),
                    new Color(1f, 1f, 1f, Random.Range(0.18f, 0.3f)));
                _clouds.Add(cloud);
                _cloudSpeed.Add(Random.Range(0.008f, 0.02f));
            }
        }

        private void Update()
        {
            float t = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;

            for (int i = 0; i < _clouds.Count; i++)
            {
                RectTransform r = _clouds[i].rectTransform;
                Vector2 a = r.anchorMin + new Vector2(_cloudSpeed[i] * dt, 0f);
                if (a.x > 1.2f)
                {
                    a = new Vector2(-0.2f, Random.Range(0.55f, 0.95f));
                }
                r.anchorMin = r.anchorMax = a;
            }

            for (int i = 0; i < _waves.Count; i++)
            {
                float k = Mathf.Sin(t * 0.8f + i * 1.7f);
                _waves[i].rectTransform.anchoredPosition = new Vector2(k * 18f, 0f);
                _waves[i].color = new Color(0.75f, 0.6f, 1f, 0.12f + 0.18f * (0.5f + 0.5f * k));
            }

            _citadelGlow.color = new Color(0.6f, 0.9f, 1f, 0.25f + 0.25f * (0.5f + 0.5f * Mathf.Sin(t * 1.6f)));
            _citadelGlow.rectTransform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(t * 1.6f));
            _lavaGlow.color = new Color(1f, 0.45f, 0.1f, 0.2f + 0.25f * (0.5f + 0.5f * Mathf.Sin(t * 2.3f + 1f)));

            for (int i = 0; i < _twinkles.Count; i++)
            {
                float k = Mathf.Max(0f, Mathf.Sin(t * 2.2f + i * 2.4f));
                _twinkles[i].color = new Color(_twinkles[i].color.r, _twinkles[i].color.g, _twinkles[i].color.b, k * k);
                _twinkles[i].rectTransform.localEulerAngles = new Vector3(0, 0, t * 40f + i * 30f);
            }

            if (t >= _nextPuff)
            {
                _nextPuff = t + Random.Range(0.25f, 0.5f);
                Vector2 source = Random.value < 0.5f ? Volcano : Volcano2;
                Image puff = Spot(ProceduralSprites.Glow(64), source, new Vector2(40, 40), new Color(0.35f, 0.32f, 0.35f, 0.6f));
                _puffs.Add(new Puff
                {
                    Image = puff,
                    Start = source,
                    Velocity = new Vector2(Random.Range(0.004f, 0.012f), Random.Range(0.025f, 0.04f)),
                    Life = Random.Range(2.2f, 3.2f),
                    Size = Random.Range(40f, 60f),
                    Color = puff.color
                });
            }
            for (int i = _puffs.Count - 1; i >= 0; i--)
            {
                Puff p = _puffs[i];
                p.Age += dt;
                if (p.Age >= p.Life || p.Image == null)
                {
                    if (p.Image != null)
                    {
                        Destroy(p.Image.gameObject);
                    }
                    _puffs.RemoveAt(i);
                    continue;
                }
                float k = p.Age / p.Life;
                RectTransform r = p.Image.rectTransform;
                r.anchorMin = r.anchorMax = p.Start + p.Velocity * p.Age;
                r.sizeDelta = Vector2.one * Mathf.Lerp(p.Size, p.Size * 3.2f, k);
                p.Image.color = new Color(p.Color.r, p.Color.g, p.Color.b, p.Color.a * (1f - k));
                _puffs[i] = p;
            }
        }
    }
}
