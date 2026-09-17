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
        private CanvasGroup _group;

        // Mascot eyes: they blink, follow the finger, widen when swapped and tremble before exploding.
        private RectTransform _eyes;
        private RectTransform[] _eyeWhites;
        private RectTransform[] _pupils;
        private float _nextBlink;
        private float _blinkTime = -1f;
        private float _surprise;
        private float _panic;

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

            RectTransform overlayRect = UIFactory.Stretch(UIFactory.Rect("Overlay", rect));
            view._overlay = overlayRect.gameObject.AddComponent<Image>();
            view._overlay.raycastTarget = false;
            view._overlay.preserveAspect = true;

            if (eyes)
            {
                view.BuildEyes(size * 0.9f);
            }
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

        /// <summary>Trembling eyes just before being cleared.</summary>
        public void Panic() => _panic = 0.4f;

        public void SetPiece(Piece piece, bool colorBlind)
        {
            Id = piece.Id;
            Piece = piece;
            gameObject.name = "Piece" + piece.Id;

            if (_eyes != null)
            {
                _eyes.gameObject.SetActive(!piece.IsStone);
            }
            if (piece.IsStone)
            {
                _body.sprite = ArtLibrary.Stone() ?? ProceduralSprites.Stone();
                float shade = piece.Hp >= 2 ? 0.7f : 1f;
                _body.color = new Color(shade, shade, shade, 1f);
                _overlay.enabled = false;
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
                    _overlay.sprite = ProceduralSprites.Stripes(true);
                    _overlay.color = new Color(1, 1, 1, 0.9f);
                    break;
                case PieceType.LineVertical:
                    _overlay.enabled = true;
                    _overlay.sprite = ProceduralSprites.Stripes(false);
                    _overlay.color = new Color(1, 1, 1, 0.9f);
                    break;
                case PieceType.AreaBomb:
                    _overlay.enabled = true;
                    _overlay.sprite = ProceduralSprites.Ring();
                    _overlay.color = new Color(1f, 0.95f, 0.6f, 1f);
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
            // Specials pulse gently so players notice them.
            if (_overlay.enabled && Piece.IsSpecial)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 5f + Id) * 0.05f;
                _overlay.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
            }
        }
    }
}
