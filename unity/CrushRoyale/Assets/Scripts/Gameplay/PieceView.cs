using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Gameplay
{
    /// <summary>Visual of one piece. Each color has its own shape so the board is readable without color vision.</summary>
    public sealed class PieceView : MonoBehaviour
    {
        private Image _body;
        private Image _overlay;

        // Countdown bomb: iron bomb badge in the corner and the moves left.
        private Image _bombBadge;
        private Text _countdown;
        private CanvasGroup _group;

        // Mascot eyes: they blink, follow the finger, widen when swapped and tremble before exploding.
        private RectTransform _eyes;
        private RectTransform[] _eyeWhites;
        private RectTransform[] _pupils;
        private float _nextBlink;
        private float _blinkTime = -1f;
        private float _surprise;
        private float _panic;

        // Depth and life: a twinkle glint that flashes now and then, a squash-and-stretch bounce on landing.
        private RectTransform _glint;
        private float _nextGlint;
        private float _glintTime = -1f;
        private float _land = -1f;
        private static bool? _reduceMotion;

        /// <summary>Board-local point the gems look at (last touch) and until when.</summary>
        public static Vector2 LookTarget;

        public static float LookUntil;

        public int Id { get; private set; }

        public Piece Piece { get; private set; }

        public Pos Cell { get; set; }

        public RectTransform Rect { get; private set; }

        public float Alpha
        {
            get => _group.alpha;
            set => _group.alpha = value;
        }

        public static PieceView Create(Transform parent, Piece piece, float size, bool colorBlind, bool eyes = false)
        {
            RectTransform rect = UIFactory.Rect("Piece" + piece.Id, parent);
            rect.sizeDelta = new Vector2(size * 0.9f, size * 0.9f);
            PieceView view = rect.gameObject.AddComponent<PieceView>();
            view.Rect = rect;
            view._group = rect.gameObject.AddComponent<CanvasGroup>();
            view._group.blocksRaycasts = false;

            view._body = rect.gameObject.AddComponent<Image>();
            view._body.raycastTarget = false;
            view._body.preserveAspect = true;

            // Soft drop shadow under the gem: the board reads as stacked jewels rather than flat stickers.
            Shadow shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.02f, 0f, 0.08f, 0.45f);
            shadow.effectDistance = new Vector2(size * 0.035f, -size * 0.06f);

            RectTransform overlayRect = UIFactory.Stretch(UIFactory.Rect("Overlay", rect));
            view._overlay = overlayRect.gameObject.AddComponent<Image>();
            view._overlay.raycastTarget = false;
            view._overlay.preserveAspect = true;

            if (eyes)
            {
                view.BuildEyes(size * 0.9f);
            }

            RectTransform glint = UIFactory.Rect("Glint", rect);
            glint.anchorMin = glint.anchorMax = new Vector2(0.3f, 0.72f);
            glint.sizeDelta = new Vector2(size * 0.42f, size * 0.42f);
            Image glintImage = glint.gameObject.AddComponent<Image>();
            glintImage.sprite = ProceduralSprites.Spark();
            glintImage.raycastTarget = false;
            glint.localScale = Vector3.zero;
            view._glint = glint;
            view._nextGlint = Time.unscaledTime + Random.Range(1f, 12f);

            view.SetPiece(piece, colorBlind);
            return view;
        }

        private void BuildEyes(float size)
        {
            _eyes = UIFactory.Rect("Eyes", Rect);
            _eyes.anchorMin = _eyes.anchorMax = new Vector2(0.5f, 0.56f);
            _eyes.sizeDelta = new Vector2(size * 0.5f, size * 0.26f);
            _eyeWhites = new RectTransform[2];
            _pupils = new RectTransform[2];
            for (int i = 0; i < 2; i++)
            {
                Image white = UIFactory.Icon(_eyes, ProceduralSprites.Circle(), Color.white, size * 0.2f);
                white.preserveAspect = false;
                white.rectTransform.anchoredPosition = new Vector2((i == 0 ? -1f : 1f) * size * 0.12f, 0f);
                Shadow rim = white.gameObject.AddComponent<Outline>();
                rim.effectColor = new Color(0.1f, 0.05f, 0.15f, 0.8f);
                rim.effectDistance = new Vector2(2, -2);
                Image pupil = UIFactory.Icon(white.rectTransform, ProceduralSprites.Circle(), new Color(0.08f, 0.05f, 0.12f, 1f), size * 0.1f);
                Image shine = UIFactory.Icon(pupil.rectTransform, ProceduralSprites.Circle(), Color.white, size * 0.035f);
                shine.rectTransform.anchoredPosition = new Vector2(size * 0.02f, size * 0.02f);
                _eyeWhites[i] = white.rectTransform;
                _pupils[i] = pupil.rectTransform;
            }
            _nextBlink = Time.unscaledTime + Random.Range(1f, 6f);
        }

        /// <summary>Wide eyes for a moment (being swapped).</summary>
        public void Surprise() => _surprise = 0.45f;

        /// <summary>Squash-and-stretch after a fall (skipped with reduced motion).</summary>
        public void Land()
        {
            if (!ReduceMotion)
            {
                _land = 0f;
            }
        }

        private static bool ReduceMotion
        {
            get
            {
                if (!_reduceMotion.HasValue || Time.frameCount % 120 == 0)
                {
                    _reduceMotion = GameRoot.Instance != null && GameRoot.Instance.Save.Settings.ReduceMotion;
                }
                return _reduceMotion.Value;
            }
        }

        /// <summary>Trembling eyes just before being cleared.</summary>
        public void Panic() => _panic = 0.4f;

        public void SetPiece(Piece piece, bool colorBlind)
        {
            Id = piece.Id;
            Piece = piece;
            gameObject.name = "Piece" + piece.Id;

            if (_eyes != null)
            {
                _eyes.gameObject.SetActive(!piece.IsBlock);
            }
            ShowCountdown(piece);
            if (piece.IsWarden)
            {
                // The eye is the tell: a warden that looked like a stone would be chipped at over ten moves and
                // heal every one of them, and the player would never understand why it was not dying.
                _body.sprite = ArtLibrary.Warden() ?? ArtLibrary.Stone() ?? ProceduralSprites.Stone();
                // Dimmer as it takes damage, so progress towards finishing it is visible.
                float wear = Mathf.Clamp01(piece.Hp / 4f);
                _body.color = new Color(1f, 0.55f + 0.45f * wear, 0.45f + 0.55f * wear, 1f);
                _overlay.enabled = false;
                return;
            }
            if (piece.IsForge)
            {
                // A forge has to look alive, because it is: it takes a gem every move. A still block would read as
                // one more stone and the player would clear it last instead of first.
                _body.sprite = ArtLibrary.Forge() ?? ArtLibrary.Blight() ?? ArtLibrary.Stone() ?? ProceduralSprites.Stone();
                _body.color = Color.white;
                _overlay.enabled = false;
                if (GetComponent<Pulse>() == null)
                {
                    gameObject.AddComponent<Pulse>().Scale = true;
                }
                return;
            }
            if (piece.IsBlight || piece.IsEgg)
            {
                Sprite block = piece.IsBlight ? ArtLibrary.Blight() : ArtLibrary.Egg(piece.Hp);
                _body.sprite = block ?? ArtLibrary.Stone() ?? ProceduralSprites.Stone();
                _body.color = block != null ? Color.white : (piece.IsBlight ? new Color(0.7f, 0.2f, 0.9f) : new Color(0.3f, 0.9f, 0.7f));
                _overlay.enabled = false;
                return;
            }
            if (piece.IsStone)
            {
                _body.sprite = ArtLibrary.Stone() ?? ProceduralSprites.Stone();
                _body.color = Color.white;
                // Two-hit stones are bound with iron bands (3D overlay), which drop off after the first hit.
                Sprite bands = piece.Hp >= 2 ? ArtLibrary.StoneBands() : null;
                _overlay.enabled = bands != null;
                if (bands != null)
                {
                    _overlay.sprite = bands;
                    _overlay.color = Color.white;
                }
                else if (piece.Hp >= 2)
                {
                    _body.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                }
                return;
            }

            // Illustrated gems already have one distinct shape per color (color-blind friendly), so they are not tinted.
            Sprite art = ArtLibrary.Gem(piece.Color);
            if (art != null)
            {
                _body.sprite = art;
                _body.color = Color.white;
            }
            else
            {
                _body.sprite = ProceduralSprites.Gem(ShapeFor(piece.Color));
                _body.color = (colorBlind ? Theme.GemsColorBlind : Theme.Gems)[(int)piece.Color];
            }

            switch (piece.Type)
            {
                case PieceType.LineHorizontal:
                    _overlay.enabled = true;
                    _overlay.sprite = ArtLibrary.Special(piece.Type) ?? ProceduralSprites.Stripes(true);
                    _overlay.color = Color.white;
                    break;
                case PieceType.LineVertical:
                    _overlay.enabled = true;
                    _overlay.sprite = ArtLibrary.Special(piece.Type) ?? ProceduralSprites.Stripes(false);
                    _overlay.color = Color.white;
                    break;
                case PieceType.Cross:
                    _overlay.enabled = true;
                    _overlay.sprite = ArtLibrary.Special(piece.Type) ?? ProceduralSprites.Ring();
                    _overlay.color = Color.white;
                    break;
                case PieceType.AreaBomb:
                    _overlay.enabled = true;
                    Sprite bomb = ArtLibrary.Special(piece.Type);
                    _overlay.sprite = bomb ?? ProceduralSprites.Ring();
                    _overlay.color = bomb != null ? Color.white : new Color(1f, 0.95f, 0.6f, 1f);
                    break;
                default:
                    _overlay.enabled = false;
                    break;
            }
        }

        private void UpdateEyes()
        {
            if (_eyes == null || !_eyes.gameObject.activeSelf)
            {
                return;
            }
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            _surprise = Mathf.Max(0f, _surprise - dt);
            _panic = Mathf.Max(0f, _panic - dt);

            float open = 1f;
            if (_blinkTime < 0f && now >= _nextBlink)
            {
                _blinkTime = 0f;
            }
            if (_blinkTime >= 0f)
            {
                _blinkTime += dt;
                open = Mathf.Abs(Mathf.Cos(Mathf.Clamp01(_blinkTime / 0.16f) * Mathf.PI));
                if (_blinkTime >= 0.16f)
                {
                    _blinkTime = -1f;
                    _nextBlink = now + Random.Range(2f, 7f);
                }
            }
            float widen = 1f + (_surprise > 0f ? 0.35f : 0f) + (_panic > 0f ? 0.2f : 0f);
            Vector2 look = Vector2.zero;
            if (now < LookUntil)
            {
                Vector2 delta = LookTarget - Rect.anchoredPosition;
                look = delta.sqrMagnitude > 1f ? delta.normalized : Vector2.zero;
            }
            else
            {
                // Idle: glance around slowly.
                look = new Vector2(Mathf.Sin(now * 0.7f + Id * 1.3f), Mathf.Sin(now * 0.5f + Id)) * 0.5f;
            }
            float jitter = _panic > 0f ? Mathf.Sin(now * 90f + Id) * 0.25f : 0f;
            for (int i = 0; i < 2; i++)
            {
                _eyeWhites[i].localScale = new Vector3(widen, widen * Mathf.Max(0.08f, open), 1f);
                float radius = _eyeWhites[i].sizeDelta.x * 0.22f;
                _pupils[i].anchoredPosition = (look + new Vector2(jitter, 0f)) * radius;
                _pupils[i].localScale = Vector3.one * (_surprise > 0f ? 0.7f : 1f);
            }
        }

        private void UpdateGlint()
        {
            if (_glint == null)
            {
                return;
            }
            float now = Time.unscaledTime;
            if (_glintTime < 0f)
            {
                if (now < _nextGlint || Piece.IsBlock)
                {
                    return;
                }
                _glintTime = 0f;
            }
            _glintTime += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_glintTime / 0.5f);
            float s = Mathf.Sin(k * Mathf.PI);
            _glint.localScale = new Vector3(s, s, 1f);
            _glint.localRotation = Quaternion.Euler(0f, 0f, k * 90f);
            if (k >= 1f)
            {
                _glint.localScale = Vector3.zero;
                _glintTime = -1f;
                _nextGlint = now + Random.Range(4f, 14f);
            }
        }

        private void ShowCountdown(Piece piece)
        {
            if (!piece.IsTimeBomb)
            {
                if (_bombBadge != null)
                {
                    _bombBadge.gameObject.SetActive(false);
                    _countdown.gameObject.SetActive(false);
                }
                return;
            }
            if (_bombBadge == null)
            {
                _bombBadge = UIFactory.Icon(Rect, ArtLibrary.BombBadge() ?? ProceduralSprites.Circle(), Color.white, 0);
                _bombBadge.raycastTarget = false;
                _bombBadge.preserveAspect = true;
                UIFactory.Anchor(_bombBadge.rectTransform, 0.38f, 0.38f, 1.02f, 1.02f);
                _countdown = UIFactory.Label(_bombBadge.transform, string.Empty, Mathf.RoundToInt(Rect.sizeDelta.y * 0.34f), Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                _countdown.raycastTarget = false;
                UIFactory.Anchor(_countdown.rectTransform, 0f, -0.05f, 0.86f, 0.8f);
                Outline outline = _countdown.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
                outline.effectDistance = new Vector2(2f, -2f);
            }
            _bombBadge.gameObject.SetActive(true);
            _countdown.gameObject.SetActive(true);
            _countdown.text = piece.Hp.ToString();
            // Red and trembling in the last three moves.
            _countdown.color = piece.Hp <= 3 ? new Color(1f, 0.3f, 0.25f) : Color.white;
            if (piece.Hp <= 3)
            {
                Panic();
            }
        }

        public static ProceduralSprites.GemShape ShapeFor(PieceColor color)
        {
            switch (color)
            {
                case PieceColor.Red: return ProceduralSprites.GemShape.Circle;
                case PieceColor.Blue: return ProceduralSprites.GemShape.Diamond;
                case PieceColor.Green: return ProceduralSprites.GemShape.Square;
                case PieceColor.Yellow: return ProceduralSprites.GemShape.Star;
                case PieceColor.Purple: return ProceduralSprites.GemShape.Hexagon;
                default: return ProceduralSprites.GemShape.Triangle;
            }
        }

        private void Update()
        {
            UpdateEyes();
            UpdateGlint();
            if (_land >= 0f)
            {
                _land += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(_land / 0.24f);
                float wobble = Mathf.Sin(k * Mathf.PI * 2f) * (1f - k);
                Rect.localScale = new Vector3(1f + wobble * 0.14f, 1f - wobble * 0.16f, 1f);
                if (k >= 1f)
                {
                    Rect.localScale = Vector3.one;
                    _land = -1f;
                }
            }
            // Specials pulse gently so players notice them.
            if (_overlay.enabled && Piece.IsSpecial)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 5f + Id) * 0.05f;
                _overlay.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
            }
        }
    }
}
