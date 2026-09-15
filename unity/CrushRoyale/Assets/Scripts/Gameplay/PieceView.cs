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

        public int Id { get; private set; }

        public Piece Piece { get; private set; }

        public Pos Cell { get; set; }

        public RectTransform Rect { get; private set; }

        public float Alpha
        {
            get => _group.alpha;
            set => _group.alpha = value;
        }

        public static PieceView Create(Transform parent, Piece piece, float size, bool colorBlind)
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

            view.SetPiece(piece, colorBlind);
            return view;
        }

        public void SetPiece(Piece piece, bool colorBlind)
        {
            Id = piece.Id;
            Piece = piece;
            gameObject.name = "Piece" + piece.Id;

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
            // Specials pulse gently so players notice them.
            if (_overlay.enabled && Piece.IsSpecial)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 5f + Id) * 0.05f;
                _overlay.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
            }
        }
    }
}
