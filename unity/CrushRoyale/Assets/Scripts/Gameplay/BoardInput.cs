using System;
using CrushRoyale.Core.Common;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CrushRoyale.Game.Gameplay
{
    /// <summary>Swipe a gem, or tap a gem then an adjacent one. Targeting mode turns taps into cell picks (Nuclear Bomb).</summary>
    public sealed class BoardInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private const float SwipeThreshold = 0.35f;

        private Pos? _pressed;
        private Vector2 _pressLocal;
        private bool _swiped;
        private Pos? _selected;

        public BoardView Board { get; set; }

        public bool Interactable { get; set; } = true;

        public bool TargetingMode { get; set; }

        public event Action<Pos, Pos> SwapRequested;

        public event Action<Pos> TargetPicked;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Interactable || Board == null || !ToLocal(eventData, out Vector2 local) || !Board.TryGetCell(local, out Pos cell))
            {
                _pressed = null;
                return;
            }
            _pressed = cell;
            _pressLocal = local;
            _swiped = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!Interactable || TargetingMode || _pressed == null || _swiped || !ToLocal(eventData, out Vector2 local))
            {
                return;
            }
            Vector2 delta = local - _pressLocal;
            if (delta.magnitude < Board.Cell * SwipeThreshold)
            {
                return;
            }

            Pos from = _pressed.Value;
            Pos to = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
                ? from.Offset(delta.x > 0 ? 1 : -1, 0)
                : from.Offset(0, delta.y > 0 ? 1 : -1);
            _swiped = true;
            ClearSelection();
            if (to.X >= 0 && to.Y >= 0 && to.X < Board.Width && to.Y < Board.Height)
            {
                SwapRequested?.Invoke(from, to);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!Interactable || _pressed == null || _swiped)
            {
                _pressed = null;
                return;
            }

            Pos tapped = _pressed.Value;
            _pressed = null;

            if (TargetingMode)
            {
                TargetingMode = false;
                TargetPicked?.Invoke(tapped);
                return;
            }

            if (_selected.HasValue && _selected.Value.IsAdjacentTo(tapped))
            {
                Pos from = _selected.Value;
                ClearSelection();
                SwapRequested?.Invoke(from, tapped);
                return;
            }

            _selected = _selected.HasValue && _selected.Value == tapped ? (Pos?)null : tapped;
            Board.SetSelected(_selected);
        }

        public void ClearSelection()
        {
            _selected = null;
            Board?.SetSelected(null);
        }

        private bool ToLocal(PointerEventData eventData, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Board.Rect, eventData.position, eventData.pressEventCamera, out local);
    }
}
