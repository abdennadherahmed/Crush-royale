using System.Collections;
using System.Collections.Generic;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Config;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Gameplay
{
    /// <summary>
    /// Board special effects: fireworks when bonus gems go off, a signature effect per power-up, combo texts, shockwaves
    /// and screen shake. A small pooled UI particle system (capped), purely visual: animation durations stay those of
    /// the balance timings, which the server's timing check relies on.
    /// </summary>
    public sealed class BoardFx : MonoBehaviour
    {
        private const int MaxParticles = 420;

        private static readonly Color Fire = new Color(1f, 0.55f, 0.12f);
        private static readonly Color Ember = new Color(1f, 0.82f, 0.3f);
        private static readonly Color Smoke = new Color(0.25f, 0.2f, 0.3f, 0.55f);

        private readonly List<Particle> _particles = new List<Particle>();
        private readonly Stack<Image> _pool = new Stack<Image>();
        private RectTransform _layer;
        private RectTransform _board;
        private float _cell;
        private float _shakeTime;
        private float _shakeDuration;
        private float _shakeStrength;
        private Vector2 _rest;

        private struct Particle
        {
            public Image Image;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Age;
            public float Life;
            public float StartSize;
            public float EndSize;
            public float Aspect;
            public float Drag;
            public float Gravity;
            public float Spin;
            public float Angle;
            public Color Color;
            public bool Stretch;
            public bool Twinkle;
        }

        public static BoardFx Create(RectTransform layer, RectTransform board, float cell)
        {
            BoardFx fx = layer.gameObject.AddComponent<BoardFx>();
            fx._layer = layer;
            fx._board = board;
            fx._cell = cell;
            return fx;
        }

        // ------------------------------------------------------------------ gem bonuses

        /// <summary>Area bomb (L/T bonus) going off: flash, double shockwave, spark shower and crackling mini bursts.</summary>
        public void Firework(Vector2 center, Color color, float power = 1f)
        {
            Color light = Color.Lerp(color, Color.white, 0.55f);
            Emit(center, Vector2.zero, 0.28f, _cell * 1.4f * power, _cell * 3.6f * power, new Color(1f, 1f, 1f, 0.95f), ProceduralSprites.Glow());
            Emit(center, Vector2.zero, 0.5f, _cell * 0.8f, _cell * 5.5f * power, light, ProceduralSprites.Ring(128));
            Emit(center, Vector2.zero, 0.7f, _cell * 0.5f, _cell * 7.5f * power, new Color(color.r, color.g, color.b, 0.6f), ProceduralSprites.Ring(128));

            int sparks = Mathf.RoundToInt(30 * power);
            for (int i = 0; i < sparks; i++)
            {
                float angle = i / (float)sparks * Mathf.PI * 2f + Random.Range(-0.15f, 0.15f);
                float speed = _cell * Random.Range(5f, 11f) * power;
                Color c = Color.Lerp(color, Color.white, Random.Range(0f, 0.6f));
                Emit(center, Dir(angle) * speed, Random.Range(0.55f, 0.95f), _cell * 0.42f, _cell * 0.06f, c, ProceduralSprites.Glow(),
                    drag: 3.2f, gravity: _cell * 4f, stretch: true);
            }
            for (int i = 0; i < 12 * power; i++)
            {
                Emit(center, Dir(Random.Range(0f, Mathf.PI * 2f)) * _cell * Random.Range(1.5f, 4f), Random.Range(0.8f, 1.3f),
                    _cell * Random.Range(0.35f, 0.6f), 0f, light, ProceduralSprites.Spark(), drag: 1.5f, gravity: _cell * 1.2f,
                    spin: Random.Range(-200f, 200f), twinkle: true);
            }
            StartCoroutine(Crackle(center, color, power));
            Shake(0.3f, 16f * power);
        }

        /// <summary>Line bonus: a blazing beam across the row / column with sparks thrown sideways.</summary>
        public void LineBlast(Vector2 center, bool horizontal, Color color, float boardSize)
        {
            Color light = Color.Lerp(color, Color.white, 0.6f);
            Vector2 axis = horizontal ? Vector2.right : Vector2.up;
            Vector2 side = horizontal ? Vector2.up : Vector2.right;
            float angle = horizontal ? 0f : 90f;

            Emit(center, Vector2.zero, 0.4f, _cell * 1.1f, _cell * 0.2f, new Color(light.r, light.g, light.b, 0.95f), ProceduralSprites.Glow(),
                aspect: boardSize * 1.15f / (_cell * 1.1f), angle: angle);
            Emit(center, Vector2.zero, 0.25f, _cell * 1.6f, _cell * 3f, Color.white, ProceduralSprites.Glow());
            for (int s = -1; s <= 1; s += 2)
            {
                Emit(center, axis * s * _cell * 26f, 0.32f, _cell * 0.9f, _cell * 0.6f, Color.white, ProceduralSprites.Glow(), stretch: true);
            }
            for (int i = 0; i < 22; i++)
            {
                Vector2 at = center + axis * Random.Range(-boardSize * 0.5f, boardSize * 0.5f);
                Vector2 velocity = side * Random.Range(-1f, 1f) * _cell * Random.Range(3f, 7f) + axis * Random.Range(-1f, 1f) * _cell;
                Emit(at, velocity, Random.Range(0.35f, 0.6f), _cell * 0.3f, _cell * 0.04f, Color.Lerp(color, Color.white, Random.value * 0.5f),
                    ProceduralSprites.Glow(), drag: 4f, gravity: _cell * 2f, stretch: true);
            }
            Shake(0.2f, 10f);
        }

        /// <summary>A bonus gem was just created (match 4/5, L/T): golden twinkles around it.</summary>
        public void BonusCreated(Vector2 center)
        {
            Emit(center, Vector2.zero, 0.35f, _cell * 0.6f, _cell * 2.2f, new Color(1f, 0.92f, 0.6f, 0.8f), ProceduralSprites.Glow());
            for (int i = 0; i < 10; i++)
            {
                float angle = i / 10f * Mathf.PI * 2f;
                Emit(center + Dir(angle) * _cell * 0.3f, Dir(angle) * _cell * Random.Range(2f, 3.5f), Random.Range(0.5f, 0.8f),
                    _cell * 0.4f, 0f, Theme.Gold, ProceduralSprites.Spark(), drag: 2.5f, spin: 180f, twinkle: true);
            }
        }

        /// <summary>The pet plays a free move: paw-print sparkles on both gems and a short caption.</summary>
        public void PetAssist(Vector2 from, Vector2 to)
        {
            Color pet = new Color(0.55f, 1f, 0.75f);
            foreach (Vector2 at in new[] { from, to })
            {
                Emit(at, Vector2.zero, 0.45f, _cell * 0.5f, _cell * 1.8f, new Color(pet.r, pet.g, pet.b, 0.8f), ProceduralSprites.Glow());
                for (int i = 0; i < 6; i++)
                {
                    Emit(at, Dir(Random.Range(0f, Mathf.PI * 2f)) * _cell * Random.Range(1.5f, 3f), Random.Range(0.4f, 0.7f),
                        _cell * 0.35f, 0f, pet, ProceduralSprites.Spark(), drag: 2f, spin: 200f, twinkle: true);
                }
            }
            Text(GameRoot.Instance.Loc.T("hud.petMove"), pet, (from + to) * 0.5f + new Vector2(0, _cell * 1.4f), 70);
        }

        /// <summary>Color blast (match 5): rainbow sparks from every cleared gem.</summary>
        public void ColorBlast(IList<Vector2> positions, Color color)
        {
            for (int i = 0; i < positions.Count && i < 30; i++)
            {
                for (int k = 0; k < 3; k++)
                {
                    Color c = Color.HSVToRGB(Random.value, 0.55f, 1f);
                    Emit(positions[i], Dir(Random.Range(0f, Mathf.PI * 2f)) * _cell * Random.Range(2f, 5f), Random.Range(0.45f, 0.8f),
                        _cell * 0.4f, 0f, c, ProceduralSprites.Spark(), drag: 2.5f, gravity: _cell * 2f, spin: 240f, twinkle: true);
                }
            }
            Shake(0.3f, 12f);
        }

        // ------------------------------------------------------------------ power-ups

        /// <summary>Signature effect of each power-up (the board clears are animated separately).</summary>
        public void PowerUp(PowerUpType type, Vector2 center, IList<Vector2> cleared)
        {
            string name = GameRoot.Instance.Loc.T("powerup." + type).ToUpperInvariant();
            switch (type)
            {
                case PowerUpType.NuclearBomb:
                    Nuke(center);
                    Text("BOOM !", Fire, center + new Vector2(0, _cell * 1.5f), 150);
                    break;
                case PowerUpType.FireStorm:
                    StartCoroutine(Meteors(cleared));
                    Text(name, Fire, Vector2.zero, 96);
                    break;
                case PowerUpType.Multiplier2x:
                    Aura(Theme.Gold, rising: true);
                    Text("x2 !", Theme.Gold, Vector2.zero, 190);
                    break;
                case PowerUpType.CoinBooster:
                    Aura(Theme.Gold, rising: true);
                    Text(name, Theme.Gold, Vector2.zero, 96);
                    break;
                case PowerUpType.CascadeInfinity:
                    Rainbow();
                    Text(name, Theme.Orbe, Vector2.zero, 96);
                    break;
                case PowerUpType.FreezingGel:
                case PowerUpType.ChronoBomb:
                    Aura(Theme.Crystal, rising: false);
                    Text(name, Theme.Crystal, Vector2.zero, 96);
                    break;
                case PowerUpType.BrightSpark:
                case PowerUpType.GoldenChain:
                    StartCoroutine(Lightning(cleared, type == PowerUpType.GoldenChain ? Theme.Gold : new Color(0.85f, 0.95f, 1f)));
                    Text(name, type == PowerUpType.GoldenChain ? Theme.Gold : Theme.Crystal, Vector2.zero, 96);
                    break;
                default:
                    Firework(center, Theme.Crystal, 1.2f);
                    Text(name, Theme.Crystal, Vector2.zero, 96);
                    break;
            }
        }

        private void Nuke(Vector2 center)
        {
            Emit(center, Vector2.zero, 0.45f, _cell * 2f, _cell * 9f, Color.white, ProceduralSprites.Glow());
            Emit(center, Vector2.zero, 0.8f, _cell * 1.5f, _cell * 6f, new Color(1f, 0.6f, 0.2f, 0.85f), ProceduralSprites.Glow());
            Emit(center, Vector2.zero, 0.55f, _cell, _cell * 10f, Ember, ProceduralSprites.Ring(128));
            Emit(center, Vector2.zero, 0.8f, _cell * 0.5f, _cell * 13f, new Color(1f, 0.45f, 0.1f, 0.7f), ProceduralSprites.Ring(128));
            Emit(center, Vector2.zero, 1.05f, _cell * 0.3f, _cell * 16f, new Color(1f, 1f, 1f, 0.35f), ProceduralSprites.Ring(128));
            for (int i = 0; i < 70; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                Color c = Color.Lerp(Fire, Ember, Random.value);
                Emit(center, Dir(angle) * _cell * Random.Range(4f, 15f), Random.Range(0.6f, 1.1f), _cell * Random.Range(0.35f, 0.7f), _cell * 0.05f, c,
                    ProceduralSprites.Glow(), drag: 2.8f, gravity: _cell * 5f, stretch: true);
            }
            // Mushroom cloud: smoke puffs rising and spreading.
            for (int i = 0; i < 16; i++)
            {
                Vector2 velocity = new Vector2(Random.Range(-2.5f, 2.5f), Random.Range(3f, 7f)) * _cell;
                Emit(center, velocity, Random.Range(1.1f, 1.6f), _cell * 1.2f, _cell * 3.5f, Smoke, ProceduralSprites.Glow(), drag: 2.2f);
            }
            Shake(0.55f, 34f);
        }

        private IEnumerator Meteors(IList<Vector2> cleared)
        {
            int count = Mathf.Clamp(cleared.Count / 3, 5, 9);
            for (int i = 0; i < count; i++)
            {
                Vector2 target = cleared.Count > 0
                    ? cleared[Random.Range(0, cleared.Count)]
                    : new Vector2(Random.Range(-3.5f, 3.5f), Random.Range(-3.5f, 3.5f)) * _cell;
                StartCoroutine(Meteor(target));
                yield return new WaitForSeconds(0.07f);
            }
        }

        private IEnumerator Meteor(Vector2 target)
        {
            Vector2 start = target + new Vector2(-_cell * 5f, _cell * 11f);
            const float duration = 0.3f;
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                Vector2 at = Vector2.Lerp(start, target, t / duration);
                Emit(at, Vector2.zero, 0.25f, _cell * 0.9f, _cell * 0.1f, Color.Lerp(Fire, Ember, Random.value), ProceduralSprites.Glow());
                Emit(at, new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * _cell, 0.4f, _cell * 0.5f, _cell * 1.1f, Smoke, ProceduralSprites.Glow());
                yield return null;
            }
            Firework(target, Fire, 0.7f);
        }

        private IEnumerator Lightning(IList<Vector2> cleared, Color color)
        {
            Vector2 previous = Vector2.zero;
            for (int i = 0; i < cleared.Count && i < 14; i++)
            {
                Vector2 at = cleared[i];
                Vector2 delta = at - previous;
                int segments = Mathf.Max(2, Mathf.RoundToInt(delta.magnitude / (_cell * 0.35f)));
                for (int k = 0; k <= segments; k++)
                {
                    Vector2 point = Vector2.Lerp(previous, at, k / (float)segments) + Random.insideUnitCircle * _cell * 0.18f;
                    Emit(point, Vector2.zero, 0.3f, _cell * 0.45f, _cell * 0.1f, color, ProceduralSprites.Glow());
                }
                Emit(at, Vector2.zero, 0.3f, _cell * 0.6f, _cell * 2f, Color.white, ProceduralSprites.Spark(), spin: 300f);
                previous = at;
                yield return new WaitForSeconds(0.025f);
            }
            Shake(0.2f, 10f);
        }

        private void Aura(Color color, bool rising)
        {
            for (int i = 0; i < 3; i++)
            {
                Emit(Vector2.zero, Vector2.zero, 0.6f + i * 0.2f, _cell * (1 + i), _cell * (11 + i * 2), new Color(color.r, color.g, color.b, 0.75f - i * 0.2f), ProceduralSprites.Ring(128));
            }
            Emit(Vector2.zero, Vector2.zero, 0.5f, _cell * 3f, _cell * 10f, new Color(color.r, color.g, color.b, 0.45f), ProceduralSprites.Glow());
            for (int i = 0; i < 45; i++)
            {
                Vector2 at = new Vector2(Random.Range(-4.5f, 4.5f), Random.Range(-4.5f, 4.5f)) * _cell;
                Vector2 velocity = rising ? new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(3f, 6f)) * _cell : Random.insideUnitCircle * _cell * 2f;
                Emit(at, velocity, Random.Range(0.7f, 1.2f), _cell * Random.Range(0.3f, 0.55f), 0f, Color.Lerp(color, Color.white, Random.value * 0.5f),
                    ProceduralSprites.Spark(), drag: 1.2f, spin: Random.Range(-240f, 240f), twinkle: true);
            }
        }

        private void Rainbow()
        {
            for (int i = 0; i < 6; i++)
            {
                Color c = Color.HSVToRGB(i / 6f, 0.6f, 1f);
                Emit(Vector2.zero, Vector2.zero, 0.5f + i * 0.08f, _cell * (0.5f + i * 0.4f), _cell * (8f + i), c, ProceduralSprites.Ring(128));
            }
            for (int i = 0; i < 40; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                Emit(Vector2.zero, Dir(angle) * _cell * Random.Range(4f, 9f), Random.Range(0.6f, 1f), _cell * 0.45f, 0f,
                    Color.HSVToRGB(Random.value, 0.6f, 1f), ProceduralSprites.Spark(), drag: 2f, spin: 200f, twinkle: true);
            }
            Shake(0.25f, 12f);
        }

        // ------------------------------------------------------------------ texts, surge, shake

        /// <summary>Cascade praise from the second chain on: Super, Great, Amazing, Divine.</summary>
        public void Combo(int cascadeLevel)
        {
            if (cascadeLevel < 2)
            {
                return;
            }
            int tier = Mathf.Min(cascadeLevel, 5);
            Color[] colors = { Theme.Crystal, Theme.Gold, Theme.Orbe, Theme.RedSurge };
            string[] keys = { "hud.combo2", "hud.combo3", "hud.combo4", "hud.combo5" };
            Text(GameRoot.Instance.Loc.T(keys[tier - 2]), colors[tier - 2], new Vector2(0, _cell * 2.5f), 96 + tier * 12);
            if (tier >= 4)
            {
                Firework(new Vector2(Random.Range(-2f, 2f), Random.Range(0f, 2f)) * _cell, colors[tier - 2], 0.8f);
            }
        }

        public void RedSurge()
        {
            Emit(Vector2.zero, Vector2.zero, 0.6f, _cell * 8f, _cell * 14f, new Color(1f, 0.18f, 0.3f, 0.55f), ProceduralSprites.Glow());
            Emit(Vector2.zero, Vector2.zero, 0.7f, _cell * 2f, _cell * 14f, Theme.RedSurge, ProceduralSprites.Ring(128));
            Text(GameRoot.Instance.Loc.T("hud.surge"), Theme.RedSurge, Vector2.zero, 120);
            Shake(0.4f, 20f);
        }

        /// <summary>Big bouncy outlined text that pops, holds and floats away.</summary>
        public void Text(string content, Color color, Vector2 position, int size)
        {
            Text label = UIFactory.Label(_layer, content, size, color, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.rectTransform.sizeDelta = new Vector2(_cell * 12f, size * 1.6f);
            label.rectTransform.anchoredPosition = position;
            Outline outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.08f, 0.03f, 0.15f, 1f);
            outline.effectDistance = new Vector2(5, -5);
            Shadow shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(0, -10);
            StartCoroutine(AnimateText(label, position));
        }

        public void Shake(float seconds, float strength)
        {
            bool active = _shakeTime > 0f;
            if (!active)
            {
                _rest = _board.anchoredPosition;
            }
            _shakeTime = Mathf.Max(_shakeTime, seconds);
            _shakeDuration = Mathf.Max(_shakeTime, 0.01f);
            _shakeStrength = Mathf.Max(active ? _shakeStrength : 0f, strength);
        }

        private IEnumerator AnimateText(Text label, Vector2 position)
        {
            RectTransform rect = label.rectTransform;
            for (float t = 0; t < 1.05f && label != null; t += Time.deltaTime)
            {
                float scale = t < 0.22f ? Ease.OutBack(t / 0.22f) * 1.15f : t < 0.32f ? Mathf.Lerp(1.15f, 1f, (t - 0.22f) / 0.1f) : 1f;
                float fade = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.35f;
                rect.localScale = Vector3.one * scale;
                rect.anchoredPosition = position + new Vector2(0, t > 0.6f ? (t - 0.6f) * _cell * 2.5f : 0f);
                rect.localEulerAngles = new Vector3(0, 0, Mathf.Sin(t * 18f) * (1f - Mathf.Clamp01(t * 3f)) * 6f);
                Color c = label.color;
                label.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(fade));
                yield return null;
            }
            if (label != null)
            {
                Destroy(label.gameObject);
            }
        }

        private IEnumerator Crackle(Vector2 center, Color color, float power)
        {
            yield return new WaitForSeconds(0.16f);
            for (int burst = 0; burst < 4; burst++)
            {
                Vector2 at = center + Random.insideUnitCircle * _cell * 2.2f * power;
                Color c = Color.Lerp(color, Color.white, 0.4f);
                Emit(at, Vector2.zero, 0.2f, _cell * 0.4f, _cell * 1.4f, Color.white, ProceduralSprites.Glow());
                for (int i = 0; i < 8; i++)
                {
                    Emit(at, Dir(i / 8f * Mathf.PI * 2f) * _cell * Random.Range(2f, 3.5f), Random.Range(0.3f, 0.5f), _cell * 0.22f, 0f, c,
                        ProceduralSprites.Glow(), drag: 3f, gravity: _cell * 2f, stretch: true);
                }
                yield return new WaitForSeconds(0.07f);
            }
        }

        // ------------------------------------------------------------------ particle core

        private void Emit(Vector2 position, Vector2 velocity, float life, float startSize, float endSize, Color color, Sprite sprite,
            float drag = 0f, float gravity = 0f, float spin = 0f, bool stretch = false, bool twinkle = false, float aspect = 1f, float angle = 0f)
        {
            if (_particles.Count >= MaxParticles)
            {
                return;
            }
            Image image = _pool.Count > 0 ? _pool.Pop() : CreateImage();
            image.gameObject.SetActive(true);
            image.sprite = sprite;
            image.color = color;
            image.rectTransform.SetAsLastSibling();
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.localScale = Vector3.one;
            _particles.Add(new Particle
            {
                Image = image,
                Position = position,
                Velocity = velocity,
                Life = Mathf.Max(0.05f, life),
                StartSize = startSize,
                EndSize = endSize,
                Aspect = aspect,
                Drag = drag,
                Gravity = gravity,
                Spin = spin,
                Angle = angle,
                Color = color,
                Stretch = stretch,
                Twinkle = twinkle
            });
            Apply(_particles[_particles.Count - 1], 0f);
        }

        private Image CreateImage()
        {
            var go = new GameObject("Fx", typeof(RectTransform), typeof(Image));
            go.layer = _layer.gameObject.layer;
            go.transform.SetParent(_layer, false);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                Particle p = _particles[i];
                p.Age += dt;
                if (p.Age >= p.Life || p.Image == null)
                {
                    if (p.Image != null)
                    {
                        p.Image.gameObject.SetActive(false);
                        _pool.Push(p.Image);
                    }
                    _particles.RemoveAt(i);
                    continue;
                }
                p.Velocity *= Mathf.Exp(-p.Drag * dt);
                p.Velocity.y -= p.Gravity * dt;
                p.Position += p.Velocity * dt;
                p.Angle += p.Spin * dt;
                _particles[i] = p;
                Apply(p, p.Age / p.Life);
            }

            if (_shakeTime > 0f && _board != null)
            {
                _shakeTime -= dt;
                if (_shakeTime <= 0f)
                {
                    _board.anchoredPosition = _rest;
                }
                else
                {
                    float strength = _shakeStrength * (_shakeTime / _shakeDuration);
                    float t = Time.time;
                    _board.anchoredPosition = _rest + new Vector2(Mathf.Sin(t * 70f) * strength, Mathf.Cos(t * 57f) * strength * 0.6f);
                }
            }
        }

        private static void Apply(Particle p, float k)
        {
            RectTransform rect = p.Image.rectTransform;
            float size = Mathf.Lerp(p.StartSize, p.EndSize, Ease.OutCubic(k));
            rect.anchoredPosition = p.Position;
            if (p.Stretch)
            {
                float speed = p.Velocity.magnitude;
                rect.sizeDelta = new Vector2(size + speed * 0.045f, size * 0.55f);
                rect.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(p.Velocity.y, p.Velocity.x) * Mathf.Rad2Deg);
            }
            else
            {
                rect.sizeDelta = new Vector2(size * p.Aspect, size);
                rect.localEulerAngles = new Vector3(0, 0, p.Angle);
            }
            float alpha = p.Color.a * (1f - k * k);
            if (p.Twinkle)
            {
                alpha *= 0.55f + 0.45f * Mathf.Sin(p.Age * 38f + p.StartSize);
            }
            p.Image.color = new Color(p.Color.r, p.Color.g, p.Color.b, Mathf.Clamp01(alpha));
        }

        private static Vector2 Dir(float radians) => new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }
}
