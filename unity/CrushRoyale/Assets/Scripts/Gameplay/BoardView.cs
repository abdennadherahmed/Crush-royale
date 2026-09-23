using System;
using System.Collections;
using System.Collections.Generic;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Gameplay
{
    /// <summary>
    /// Renders a <see cref="GameBoard"/> and animates action outcomes step by step using the event log produced by the
    /// simulation (clears, bonuses, stones, ice, falls, refills, shuffles). Animation durations match the balance timings,
    /// which is what the server's anti-bot timing check expects.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        private const float ClearFraction = 0.4f;

        private readonly Dictionary<int, PieceView> _pieces = new Dictionary<int, PieceView>();
        private RectTransform _cellsLayer;
        private RectTransform _iceLayer;

        /// <summary>Chains are drawn over the pieces, unlike ice which sits behind them: they hold the gem, not cover it.</summary>
        private RectTransform _chainLayer;
        private RectTransform _piecesLayer;
        private RectTransform _fxLayer;
        private Image[] _ice;
        private int[] _iceLayers;
        private Image[] _chains;
        private int[] _chainLinks;
        private Image[] _curses;
        private bool[] _cursed;
        private Image _hintA;
        private Image _hintB;
        private TimingBalance _timing;
        private BoardFx _fx;
        private Image _frame;

        public RectTransform Rect { get; private set; }

        public BoardFx Fx => _fx;

        /// <summary>The pet starts its free move (board-local positions of the two swapped gems).</summary>
        public event Action<Vector2, Vector2> PetMoveStarted;

        /// <summary>Last finger position on the board (gems look at it for a moment).</summary>
        public void LookAt(Vector2 local)
        {
            PieceView.LookTarget = local;
            PieceView.LookUntil = Time.unscaledTime + 2.5f;
        }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public float Cell { get; private set; }

        public bool ColorBlind { get; set; }

        /// <summary>Ghost boards animate faster and never show hints.</summary>
        public bool IsGhost { get; private set; }

        public static BoardView Create(Transform parent, float pixelSize, bool ghost)
        {
            RectTransform rect = UIFactory.Rect(ghost ? "GhostBoard" : "Board", parent);
            rect.sizeDelta = new Vector2(pixelSize, pixelSize);
            BoardView view = rect.gameObject.AddComponent<BoardView>();
            view.Rect = rect;
            view.IsGhost = ghost;

            Image frame = rect.gameObject.AddComponent<Image>();
            view._frame = frame;
            frame.sprite = ProceduralSprites.RoundedRect(28);
            frame.type = Image.Type.Sliced;
            frame.color = ghost ? new Color(0.1f, 0.08f, 0.2f, 0.75f) : new Color(0.08f, 0.06f, 0.16f, 0.92f);
            frame.raycastTarget = !ghost;

            view._cellsLayer = UIFactory.Stretch(UIFactory.Rect("Cells", rect));
            view._iceLayer = UIFactory.Stretch(UIFactory.Rect("Ice", rect));
            view._piecesLayer = UIFactory.Stretch(UIFactory.Rect("Pieces", rect));
            view._chainLayer = UIFactory.Stretch(UIFactory.Rect("Chains", rect));
            view._fxLayer = UIFactory.Stretch(UIFactory.Rect("Fx", rect));
            return view;
        }

        public void Bind(GameBoard board, GameBalance balance, bool colorBlind)
        {
            _timing = balance.Timing;
            ColorBlind = colorBlind;
            Width = board.Width;
            Height = board.Height;
            Cell = Rect.sizeDelta.x / Mathf.Max(Width, Height);

            UIFactory.Clear(_cellsLayer);
            UIFactory.Clear(_iceLayer);
            // The chain layer holds the chains and the curses: leaving it out here stacked a second set of images
            // over the first every time a board was rebound (a continue, a shuffle, a new attempt).
            UIFactory.Clear(_chainLayer);
            UIFactory.Clear(_piecesLayer);
            _pieces.Clear();

            _ice = new Image[Width * Height];
            _iceLayers = new int[Width * Height];
            _chains = new Image[Width * Height];
            _chainLinks = new int[Width * Height];
            _curses = new Image[Width * Height];
            _cursed = new bool[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var pos = new Pos(x, y);
                    RectTransform cell = UIFactory.Rect("Cell", _cellsLayer);
                    cell.sizeDelta = new Vector2(Cell * 0.96f, Cell * 0.96f);
                    cell.anchoredPosition = CellPosition(pos);
                    Image bg = cell.gameObject.AddComponent<Image>();
                    bg.sprite = ProceduralSprites.RoundedRect(16);
                    bg.type = Image.Type.Sliced;
                    bg.color = (x + y) % 2 == 0 ? new Color(1, 1, 1, 0.06f) : new Color(1, 1, 1, 0.03f);
                    bg.raycastTarget = false;

                    RectTransform curse = UIFactory.Rect("Curse", _chainLayer);
                    curse.sizeDelta = cell.sizeDelta;
                    curse.anchoredPosition = cell.anchoredPosition;
                    Image curseImage = curse.gameObject.AddComponent<Image>();
                    curseImage.sprite = ArtLibrary.Cursed();
                    curseImage.raycastTarget = false;
                    curseImage.enabled = false;
                    _curses[y * Width + x] = curseImage;

                    RectTransform chain = UIFactory.Rect("Chain", _chainLayer);
                    chain.sizeDelta = cell.sizeDelta;
                    chain.anchoredPosition = cell.anchoredPosition;
                    Image chainImage = chain.gameObject.AddComponent<Image>();
                    chainImage.sprite = ArtLibrary.Chain();
                    chainImage.raycastTarget = false;
                    chainImage.enabled = false;
                    _chains[y * Width + x] = chainImage;

                    RectTransform ice = UIFactory.Rect("Ice", _iceLayer);
                    ice.sizeDelta = cell.sizeDelta;
                    ice.anchoredPosition = cell.anchoredPosition;
                    Image iceImage = ice.gameObject.AddComponent<Image>();
                    Sprite iceArt = ArtLibrary.Ice();
                    if (iceArt != null)
                    {
                        iceImage.sprite = iceArt;
                    }
                    else
                    {
                        iceImage.sprite = ProceduralSprites.RoundedRect(16);
                        iceImage.type = Image.Type.Sliced;
                    }
                    iceImage.raycastTarget = false;
                    _ice[y * Width + x] = iceImage;
                }
            }

            if (!IsGhost)
            {
                if (_fx == null)
                {
                    _fx = BoardFx.Create(_fxLayer, Rect, Cell);
                }
                _hintA = UIFactory.Icon(_fxLayer, ProceduralSprites.Ring(), new Color(1, 1, 1, 0), Cell);
                _hintB = UIFactory.Icon(_fxLayer, ProceduralSprites.Ring(), new Color(1, 1, 1, 0), Cell);
            }
            SyncTo(board);
        }

        public Vector2 CellPosition(Pos p) => new Vector2((p.X + 0.5f - Width / 2f) * Cell, (p.Y + 0.5f - Height / 2f) * Cell);

        public bool TryGetCell(Vector2 localPoint, out Pos pos)
        {
            int x = Mathf.FloorToInt(localPoint.x / Cell + Width / 2f);
            int y = Mathf.FloorToInt(localPoint.y / Cell + Height / 2f);
            pos = new Pos(x, y);
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        /// <summary>Hard re-synchronization with the simulation (creates/destroys/moves views by piece id).</summary>
        public void SyncTo(GameBoard board)
        {
            var alive = new HashSet<int>();
            foreach (Pos p in board.AllPositions())
            {
                Piece piece = board[p];
                if (piece.IsEmpty)
                {
                    continue;
                }
                alive.Add(piece.Id);
                if (!_pieces.TryGetValue(piece.Id, out PieceView view))
                {
                    view = PieceView.Create(_piecesLayer, piece, Cell, ColorBlind, eyes: !IsGhost);
                    _pieces[piece.Id] = view;
                }
                else if (!view.Piece.Equals(piece))
                {
                    view.SetPiece(piece, ColorBlind);
                }
                view.Cell = p;
                view.Rect.anchoredPosition = CellPosition(p);
                view.Rect.localScale = Vector3.one;
                view.Alpha = 1f;
            }

            var dead = new List<int>();
            foreach (KeyValuePair<int, PieceView> kv in _pieces)
            {
                if (!alive.Contains(kv.Key))
                {
                    dead.Add(kv.Key);
                }
            }
            foreach (int id in dead)
            {
                Destroy(_pieces[id].gameObject);
                _pieces.Remove(id);
            }

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    _iceLayers[y * Width + x] = board.IceAt(new Pos(x, y));
                    RefreshIce(x, y);
                    _chainLinks[y * Width + x] = board.ChainAt(new Pos(x, y));
                    RefreshChain(x, y);
                    _cursed[y * Width + x] = board.IsCursed(new Pos(x, y));
                    RefreshCurse(x, y);
                }
            }
        }

        /// <summary>Colors the frame and the checkerboard cells with the equipped board skin.</summary>
        public void ApplySkin(string boardSkin)
        {
            (Color frame, Color cellA, Color cellB) = CosmeticLook.Board(boardSkin);
            if (_frame != null && !IsGhost)
            {
                _frame.color = frame;
            }
            for (int i = 0; i < _cellsLayer.childCount; i++)
            {
                Image cell = _cellsLayer.GetChild(i).GetComponent<Image>();
                if (cell != null && Width > 0)
                {
                    int x = i % Width;
                    int y = i / Width;
                    cell.color = (x + y) % 2 == 0 ? cellA : cellB;
                }
            }
        }

        private Coroutine _hintRoutine;

        /// <summary>Idle hint: pulsing rings on both cells and the two gems nudging toward each other.</summary>
        public void ShowHint(Move move)
        {
            if (_hintA == null)
            {
                return;
            }
            ClearHint();
            _hintA.rectTransform.anchoredPosition = CellPosition(move.From);
            _hintB.rectTransform.anchoredPosition = CellPosition(move.To);
            _hintRoutine = StartCoroutine(HintPulse(move));
        }

        public void ClearHint()
        {
            if (_hintRoutine != null)
            {
                StopCoroutine(_hintRoutine);
                _hintRoutine = null;
                foreach (PieceView view in _pieces.Values)
                {
                    if (view != null)
                    {
                        view.Rect.anchoredPosition = CellPosition(view.Cell);
                        view.Rect.localScale = Vector3.one;
                    }
                }
            }
            if (_hintA != null)
            {
                _hintA.color = _hintB.color = new Color(1, 1, 1, 0);
                _hintA.rectTransform.localScale = _hintB.rectTransform.localScale = Vector3.one;
            }
        }

        private PieceView PieceAt(Pos cell)
        {
            foreach (PieceView view in _pieces.Values)
            {
                if (view != null && view.Cell.Equals(cell))
                {
                    return view;
                }
            }
            return null;
        }

        private IEnumerator HintPulse(Move move)
        {
            PieceView a = PieceAt(move.From);
            PieceView b = PieceAt(move.To);
            Vector2 from = CellPosition(move.From);
            Vector2 to = CellPosition(move.To);
            Vector2 toward = (to - from) * 0.16f;
            float start = Time.unscaledTime;
            while (true)
            {
                float t = Time.unscaledTime - start;
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 5.5f);
                _hintA.color = _hintB.color = new Color(1f, 0.93f, 0.45f, 0.45f + 0.5f * pulse);
                _hintA.rectTransform.localScale = _hintB.rectTransform.localScale = Vector3.one * (1f + 0.1f * pulse);

                // Every 1.4 s the two gems lean into each other twice, like a swap about to happen.
                float cycle = t % 1.4f;
                float nudge = cycle < 0.5f ? Mathf.Sin(cycle / 0.5f * Mathf.PI * 2f) : 0f;
                nudge = Mathf.Max(0f, nudge);
                if (a != null)
                {
                    a.Rect.anchoredPosition = from + toward * nudge;
                    a.Rect.localScale = Vector3.one * (1f + 0.08f * nudge);
                }
                if (b != null)
                {
                    b.Rect.anchoredPosition = to - toward * nudge;
                    b.Rect.localScale = Vector3.one * (1f + 0.08f * nudge);
                }
                yield return null;
            }
        }

        public void SetSelected(Pos? cell)
        {
            if (_hintA == null)
            {
                return;
            }
            if (cell.HasValue && _hintRoutine != null)
            {
                ClearHint();
            }
            if (cell.HasValue)
            {
                _hintA.rectTransform.anchoredPosition = CellPosition(cell.Value);
                _hintA.color = new Color(0.45f, 0.85f, 1f, 0.95f);
            }
            else
            {
                _hintA.color = new Color(1, 1, 1, 0);
            }
        }

        public IEnumerator PlayInvalidSwap(Pos a, Pos b)
        {
            PieceView pa = FindAt(a);
            PieceView pb = FindAt(b);
            if (pa == null || pb == null)
            {
                yield break;
            }
            Vector2 posA = CellPosition(a);
            Vector2 posB = CellPosition(b);
            float half = (_timing?.InvalidSwapAnimationMs ?? 300) / 2000f;
            pa.Rect.SetAsLastSibling();
            yield return CoroutineTask.Tween(half, k =>
            {
                float e = Ease.OutCubic(k) * 0.45f;
                pa.Rect.anchoredPosition = Vector2.Lerp(posA, posB, e);
                pb.Rect.anchoredPosition = Vector2.Lerp(posB, posA, e);
                pa.Rect.localScale = Vector3.one * (1f + 0.12f * k);
            });
            yield return CoroutineTask.Tween(half, k =>
            {
                float e = (1 - Ease.OutCubic(k)) * 0.45f;
                pa.Rect.anchoredPosition = Vector2.Lerp(posA, posB, e);
                pb.Rect.anchoredPosition = Vector2.Lerp(posB, posA, e);
                pa.Rect.localScale = Vector3.one * (1.12f - 0.12f * k);
            });
            pa.Rect.localScale = Vector3.one;
        }

        /// <summary>Animates an accepted action, then snaps to the final simulated board.</summary>
        public IEnumerator PlayOutcome(ActionOutcome outcome, GameBoard finalBoard, Action<ResolutionStep> onStep)
        {
            float speed = IsGhost ? 2.5f : 1f;
            PlayerAction action = outcome.Action;

            if (action.Type == ActionType.Swap)
            {
                PieceView pa = FindAt(action.From);
                PieceView pb = FindAt(action.To);
                if (pa != null && pb != null)
                {
                    pa.Surprise();
                    pb.Surprise();
                    Vector2 a = CellPosition(action.From);
                    Vector2 b = CellPosition(action.To);
                    // 2.5D pass-over: the dragged gem rises above the other (bigger, on top), the other dips under.
                    pa.Rect.SetAsLastSibling();
                    yield return CoroutineTask.Tween(_timing.SwapAnimationMs / 1000f / speed, k =>
                    {
                        float e = Ease.OutCubic(k);
                        float arc = Mathf.Sin(k * Mathf.PI);
                        pa.Rect.anchoredPosition = Vector2.Lerp(a, b, e);
                        pb.Rect.anchoredPosition = Vector2.Lerp(b, a, e);
                        pa.Rect.localScale = Vector3.one * (1f + 0.18f * arc);
                        pb.Rect.localScale = Vector3.one * (1f - 0.1f * arc);
                    });
                    pa.Rect.localScale = Vector3.one;
                    pb.Rect.localScale = Vector3.one;
                    pa.Cell = action.To;
                    pb.Cell = action.From;
                }
            }
            else if (action.Type == ActionType.PowerUp)
            {
                Vector2 center = action.HasTarget ? CellPosition(action.Target) : Vector2.zero;
                if (_fx != null)
                {
                    var cleared = new List<Vector2>();
                    if (outcome.Resolution != null && outcome.Resolution.Steps.Count > 0)
                    {
                        foreach (ClearedPiece piece in outcome.Resolution.Steps[0].Cleared)
                        {
                            cleared.Add(CellPosition(piece.Position));
                        }
                    }
                    _fx.PowerUp(action.PowerUp, center, cleared);
                }
                yield return Flash(center, _timing.PowerUpAnimationMs / 1000f / speed);
            }

            if (_fx != null && outcome.RedSurgeActivated)
            {
                _fx.RedSurge();
            }

            if (outcome.Resolution != null)
            {
                foreach (ResolutionStep step in outcome.Resolution.Steps)
                {
                    onStep?.Invoke(step);
                    yield return PlayStep(step, _timing.CascadeStepMs / 1000f / speed);
                }

                if (outcome.Resolution.Shuffled && outcome.Resolution.BoardAfterShuffle != null)
                {
                    yield return MoveTo(outcome.Resolution.BoardAfterShuffle, _timing.ShuffleAnimationMs / 1000f / speed);
                }
            }

            // The pet's free move follows immediately (same timings as a player swap, as the simulation counts them).
            if (outcome.PetMove.HasValue && outcome.PetResolution != null)
            {
                Move petMove = outcome.PetMove.Value;
                _fx?.PetAssist(CellPosition(petMove.From), CellPosition(petMove.To));
                PetMoveStarted?.Invoke(CellPosition(petMove.From), CellPosition(petMove.To));
                PieceView pa = FindAt(petMove.From);
                PieceView pb = FindAt(petMove.To);
                if (pa != null && pb != null)
                {
                    Vector2 a = CellPosition(petMove.From);
                    Vector2 b = CellPosition(petMove.To);
                    yield return CoroutineTask.Tween(_timing.SwapAnimationMs / 1000f / speed, k =>
                    {
                        float e = Ease.OutCubic(k);
                        pa.Rect.anchoredPosition = Vector2.Lerp(a, b, e);
                        pb.Rect.anchoredPosition = Vector2.Lerp(b, a, e);
                    });
                    pa.Cell = petMove.To;
                    pb.Cell = petMove.From;
                }
                else
                {
                    yield return CoroutineTask.Tween(_timing.SwapAnimationMs / 1000f / speed, _ => { });
                }
                foreach (ResolutionStep step in outcome.PetResolution.Steps)
                {
                    onStep?.Invoke(step);
                    yield return PlayStep(step, _timing.CascadeStepMs / 1000f / speed);
                }
                if (outcome.PetResolution.Shuffled && outcome.PetResolution.BoardAfterShuffle != null)
                {
                    yield return MoveTo(outcome.PetResolution.BoardAfterShuffle, _timing.ShuffleAnimationMs / 1000f / speed);
                }
            }

            if (outcome.BossStones.Count > 0)
            {
                yield return Shake(0.35f / speed, 22f);
            }

            SyncTo(finalBoard);
        }

        private IEnumerator PlayStep(ResolutionStep step, float seconds)
        {
            float clearTime = seconds * ClearFraction;
            float fallTime = seconds - clearTime;

            var vanishing = new List<PieceView>();
            foreach (ClearedPiece cleared in step.Cleared)
            {
                if (_pieces.TryGetValue(cleared.Piece.Id, out PieceView view))
                {
                    vanishing.Add(view);
                    if (!IsGhost && vanishing.Count <= 24)
                    {
                        StartCoroutine(Burst(CellPosition(cleared.Position), (ColorBlind ? Theme.GemsColorBlind : Theme.Gems)[(int)cleared.Piece.Color]));
                    }
                }
            }
            if (_fx != null)
            {
                PlayStepFx(step);
            }
            foreach (StoneHit hit in step.StoneHits)
            {
                if (!_pieces.TryGetValue(hit.PieceId, out PieceView stone))
                {
                    continue;
                }
                if (hit.Destroyed)
                {
                    vanishing.Add(stone);
                    if (!IsGhost && hit.Type != PieceType.Stone)
                    {
                        // Corrupted crystal shatters in magenta, a dragon egg in gold.
                        StartCoroutine(Burst(CellPosition(hit.Position), hit.Type == PieceType.Blight ? new Color(0.85f, 0.2f, 1f) : new Color(1f, 0.8f, 0.3f)));
                    }
                }
                else
                {
                    stone.SetPiece(stone.Piece.WithHp((byte)hit.RemainingHp), ColorBlind);
                }
            }
            foreach (Pos ice in step.IceBroken)
            {
                int index = ice.Y * Width + ice.X;
                _iceLayers[index] = Mathf.Max(0, _iceLayers[index] - 1);
                RefreshIce(ice.X, ice.Y);
            }
            foreach (Pos chain in step.ChainsBroken)
            {
                OnChainBroken(chain);
            }
            foreach (Pos curse in step.CursesTriggered)
            {
                OnCurseTriggered(curse);
            }

            foreach (PieceView v in vanishing)
            {
                v?.Panic();
            }
            yield return CoroutineTask.Tween(clearTime, k =>
            {
                float s = 1f - Ease.InQuad(k);
                foreach (PieceView v in vanishing)
                {
                    if (v != null)
                    {
                        v.Rect.localScale = new Vector3(s, s, 1);
                        v.Alpha = s;
                    }
                }
            });
            foreach (PieceView v in vanishing)
            {
                if (v != null)
                {
                    _pieces.Remove(v.Id);
                    Destroy(v.gameObject);
                }
            }

            var appearing = new List<PieceView>();
            foreach (PieceSpawn special in step.SpecialsCreated)
            {
                PieceView view = PieceView.Create(_piecesLayer, special.Piece, Cell, ColorBlind);
                view.Cell = special.Position;
                view.Rect.anchoredPosition = CellPosition(special.Position);
                view.Rect.localScale = Vector3.zero;
                _pieces[special.Piece.Id] = view;
                appearing.Add(view);
                if (!IsGhost)
                {
                    StartCoroutine(SpecialGlow(CellPosition(special.Position)));
                    _fx?.BonusCreated(CellPosition(special.Position));
                }
            }
            // Big clears (a line/bomb going off, or a large combo) shake the board a little.
            if (_fx != null && step.Cleared.Count >= 10)
            {
                _fx.Shake(0.18f, 12f);
            }

            var moves = new List<(PieceView View, Vector2 From, Vector2 To)>();
            foreach (PieceFall fall in step.Falls)
            {
                if (_pieces.TryGetValue(fall.PieceId, out PieceView view))
                {
                    moves.Add((view, view.Rect.anchoredPosition, CellPosition(fall.To)));
                    view.Cell = fall.To;
                }
            }
            foreach (PieceSpawn refill in step.Refills)
            {
                PieceView view = PieceView.Create(_piecesLayer, refill.Piece, Cell, ColorBlind);
                Vector2 start = CellPosition(new Pos(refill.Position.X, refill.SpawnRow));
                view.Rect.anchoredPosition = start;
                view.Cell = refill.Position;
                _pieces[refill.Piece.Id] = view;
                moves.Add((view, start, CellPosition(refill.Position)));
            }

            yield return CoroutineTask.Tween(fallTime, k =>
            {
                float e = Ease.InQuad(k);
                foreach ((PieceView view, Vector2 from, Vector2 to) in moves)
                {
                    if (view != null)
                    {
                        view.Rect.anchoredPosition = Vector2.LerpUnclamped(from, to, e);
                    }
                }
                float pop = Ease.OutBack(k);
                foreach (PieceView view in appearing)
                {
                    if (view != null)
                    {
                        view.Rect.localScale = new Vector3(pop, pop, 1);
                    }
                }
            });
            foreach ((PieceView view, Vector2 from, Vector2 to) in moves)
            {
                if (view != null && (from - to).sqrMagnitude > 1f)
                {
                    view.Land();
                }
            }
        }

        private IEnumerator MoveTo(GameBoard target, float seconds)
        {
            var moves = new List<(PieceView View, Vector2 From, Vector2 To)>();
            foreach (Pos p in target.AllPositions())
            {
                Piece piece = target[p];
                if (!piece.IsEmpty && _pieces.TryGetValue(piece.Id, out PieceView view))
                {
                    moves.Add((view, view.Rect.anchoredPosition, CellPosition(p)));
                    view.Cell = p;
                    if (!view.Piece.Equals(piece))
                    {
                        view.SetPiece(piece, ColorBlind);
                    }
                }
            }
            yield return CoroutineTask.Tween(seconds, k =>
            {
                float e = Ease.OutCubic(k);
                foreach ((PieceView view, Vector2 from, Vector2 to) in moves)
                {
                    if (view != null)
                    {
                        Vector2 mid = Vector2.zero;
                        view.Rect.anchoredPosition = k < 0.5f ? Vector2.Lerp(from, mid, e * 1.6f) : Vector2.Lerp(mid, to, e);
                    }
                }
            });
        }

        /// <summary>Power-up impact: bright core flash, expanding shockwave ring and a short board shake.</summary>
        private IEnumerator Flash(Vector2 center, float seconds)
        {
            Image flash = UIFactory.Icon(_fxLayer, ProceduralSprites.Circle(), new Color(1f, 0.9f, 0.6f, 0.8f), Cell);
            flash.rectTransform.anchoredPosition = center;
            Image wave = UIFactory.Icon(_fxLayer, ProceduralSprites.Ring(128), Theme.Crystal, Cell);
            wave.rectTransform.anchoredPosition = center;
            _fx?.Shake(Mathf.Min(0.3f, seconds), 14f);
            yield return CoroutineTask.Tween(seconds, k =>
            {
                float size = Cell * (1 + k * 5);
                flash.rectTransform.sizeDelta = new Vector2(size, size);
                flash.color = new Color(1f, 0.9f, 0.6f, 0.8f * (1 - k));
                float waveSize = Cell * (1 + Ease.OutCubic(k) * 8);
                wave.rectTransform.sizeDelta = new Vector2(waveSize, waveSize);
                wave.color = new Color(Theme.Crystal.r, Theme.Crystal.g, Theme.Crystal.b, 1 - k);
            });
            Destroy(flash.gameObject);
            Destroy(wave.gameObject);
        }

        /// <summary>Waits the given time while the board shakes (the shake itself is driven by <see cref="BoardFx"/>).</summary>
        private IEnumerator Shake(float seconds, float strength)
        {
            _fx?.Shake(seconds, strength);
            yield return CoroutineTask.Tween(seconds, _ => { });
        }

        /// <summary>Fireworks for bonus gems going off, rainbow sparks for color blasts, praise for long cascades.</summary>
        private void PlayStepFx(ResolutionStep step)
        {
            float boardSize = Cell * Mathf.Max(Width, Height);
            Color[] palette = ColorBlind ? Theme.GemsColorBlind : Theme.Gems;
            List<Vector2> colorBlast = null;
            Color blastColor = Theme.Orbe;
            foreach (ClearedPiece cleared in step.Cleared)
            {
                Vector2 at = CellPosition(cleared.Position);
                Color color = palette[(int)cleared.Piece.Color];
                switch (cleared.Piece.Type)
                {
                    case PieceType.LineHorizontal:
                        _fx.LineBlast(at, true, color, boardSize);
                        break;
                    case PieceType.LineVertical:
                        _fx.LineBlast(at, false, color, boardSize);
                        break;
                    case PieceType.AreaBomb:
                        _fx.Firework(at, color, 1.25f);
                        break;
                    case PieceType.Cross:
                        _fx.LineBlast(at, true, color, boardSize);
                        _fx.LineBlast(at, false, color, boardSize);
                        break;
                }
                if (cleared.Cause == ClearCause.ColorBlast)
                {
                    colorBlast = colorBlast ?? new List<Vector2>();
                    colorBlast.Add(at);
                    blastColor = color;
                }
            }
            if (colorBlast != null)
            {
                _fx.ColorBlast(colorBlast, blastColor);
            }
            _fx.Combo(step.CascadeLevel);
        }

        /// <summary>Gem shatter: a white pop ring, star sparks that fly out and fall, and small colored shards.</summary>
        private IEnumerator Burst(Vector2 center, Color color)
        {
            const int particles = 8;
            Color light = Color.Lerp(color, Color.white, 0.55f);
            var images = new Image[particles + 1];
            var velocities = new Vector2[particles];
            var spins = new float[particles];

            images[particles] = UIFactory.Icon(_fxLayer, ProceduralSprites.Ring(64), light, Cell * 0.5f);
            images[particles].rectTransform.anchoredPosition = center;

            for (int i = 0; i < particles; i++)
            {
                bool star = i % 2 == 0;
                Sprite sprite = star ? ProceduralSprites.Gem(ProceduralSprites.GemShape.Star, 32) : ProceduralSprites.Gem(ProceduralSprites.GemShape.Diamond, 32);
                images[i] = UIFactory.Icon(_fxLayer, sprite, star ? light : color, Cell * (star ? 0.26f : 0.16f));
                images[i].rectTransform.anchoredPosition = center;
                float angle = (i / (float)particles) * Mathf.PI * 2 + UnityEngine.Random.Range(-0.3f, 0.3f);
                float speed = Cell * UnityEngine.Random.Range(1.3f, 2.1f);
                velocities[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) + 0.6f) * speed;
                spins[i] = UnityEngine.Random.Range(-540f, 540f);
            }

            const float duration = 0.55f;
            yield return CoroutineTask.Tween(duration, k =>
            {
                float t = k * duration;
                for (int i = 0; i < particles; i++)
                {
                    if (images[i] == null)
                    {
                        continue;
                    }
                    // Ballistic arc: initial burst slowed by gravity.
                    Vector2 offset = velocities[i] * t + new Vector2(0, -Cell * 5.5f) * t * t;
                    images[i].rectTransform.anchoredPosition = center + offset;
                    images[i].rectTransform.localEulerAngles = new Vector3(0, 0, spins[i] * t);
                    float scale = 1f - Ease.InQuad(k) * 0.7f;
                    images[i].rectTransform.localScale = new Vector3(scale, scale, 1);
                    Color c = images[i].color;
                    images[i].color = new Color(c.r, c.g, c.b, 1 - Ease.InQuad(k));
                }
                if (images[particles] != null)
                {
                    float ring = Cell * (0.5f + Ease.OutCubic(Mathf.Min(1f, k * 2f)) * 0.9f);
                    images[particles].rectTransform.sizeDelta = new Vector2(ring, ring);
                    images[particles].color = new Color(light.r, light.g, light.b, 0.9f * (1 - Mathf.Min(1f, k * 2f)));
                }
            });
            foreach (Image image in images)
            {
                if (image != null)
                {
                    Destroy(image.gameObject);
                }
            }
        }

        /// <summary>Golden glow ring that pulses out when a special piece (line, bomb) is created.</summary>
        private IEnumerator SpecialGlow(Vector2 center)
        {
            Image ring = UIFactory.Icon(_fxLayer, ProceduralSprites.Ring(96), Theme.Gold, Cell);
            ring.rectTransform.anchoredPosition = center;
            yield return CoroutineTask.Tween(0.45f, k =>
            {
                float size = Cell * (0.8f + Ease.OutCubic(k) * 1.2f);
                ring.rectTransform.sizeDelta = new Vector2(size, size);
                ring.color = new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 1 - k);
            });
            Destroy(ring.gameObject);
        }

        private PieceView FindAt(Pos pos)
        {
            foreach (PieceView view in _pieces.Values)
            {
                if (view.Cell == pos)
                {
                    return view;
                }
            }
            return null;
        }

        /// <summary>
        /// Shows the chain holding a gem, and how tight it still is.
        ///
        /// Drawn over the piece rather than behind it, because a chain does not cover a gem, it holds it: the player
        /// has to read at a glance which gems they cannot move.
        /// </summary>
        private void RefreshChain(int x, int y)
        {
            Image chain = _chains[y * Width + x];
            if (chain == null)
            {
                return;
            }
            int links = _chainLinks[y * Width + x];
            chain.enabled = links > 0 && chain.sprite != null;
            chain.color = links >= 2 ? Color.white : new Color(1f, 1f, 1f, 0.72f);
        }

        /// <summary>
        /// Marks a gem the player must not match.
        ///
        /// Drawn over the piece and left breathing, because the cost of missing one is a move: a curse the player
        /// only notices after paying for it teaches nothing.
        /// </summary>
        private void RefreshCurse(int x, int y)
        {
            Image curse = _curses[y * Width + x];
            if (curse == null)
            {
                return;
            }
            bool on = _cursed[y * Width + x] && curse.sprite != null;
            curse.enabled = on;
            if (on && curse.GetComponent<Pulse>() == null)
            {
                curse.gameObject.AddComponent<Pulse>();
            }
        }

        /// <summary>Called when the player destroyed a cursed gem and paid for it.</summary>
        public void OnCurseTriggered(Pos p)
        {
            int i = p.Y * Width + p.X;
            if (i < 0 || i >= _cursed.Length)
            {
                return;
            }
            _cursed[i] = false;
            RefreshCurse(p.X, p.Y);
            StartCoroutine(Burst(CellPosition(p), new Color(0.75f, 0.2f, 0.95f)));
        }

        /// <summary>Called when a clear next to a chained cell snapped one of its links.</summary>
        public void OnChainBroken(Pos p)
        {
            int i = p.Y * Width + p.X;
            if (i < 0 || i >= _chainLinks.Length)
            {
                return;
            }
            _chainLinks[i] = Mathf.Max(0, _chainLinks[i] - 1);
            RefreshChain(p.X, p.Y);
            StartCoroutine(Burst(CellPosition(p), new Color(0.85f, 0.78f, 0.6f)));
        }

        private void RefreshIce(int x, int y)
        {
            int layers = _iceLayers[y * Width + x];
            Image ice = _ice[y * Width + x];
            ice.enabled = layers > 0;
            // The illustrated cube sits behind the piece: thicker ice is more opaque.
            ice.color = ice.type == Image.Type.Sliced
                ? new Color(0.75f, 0.92f, 1f, 0.18f + 0.18f * layers)
                : new Color(1f, 1f, 1f, 0.45f + 0.25f * layers);
        }
    }
}
