using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Board
{
    /// <summary>Why a piece left the board.</summary>
    public enum ClearCause : byte
    {
        Match = 0,
        LineBlast = 1,
        AreaBlast = 2,
        ColorBlast = 3,
        PowerUp = 4
    }

    public readonly struct ClearedPiece
    {
        public readonly Pos Position;
        public readonly Piece Piece;
        public readonly ClearCause Cause;

        public ClearedPiece(Pos position, Piece piece, ClearCause cause)
        {
            Position = position;
            Piece = piece;
            Cause = cause;
        }
    }

    public readonly struct PieceFall
    {
        public readonly int PieceId;
        public readonly Pos From;
        public readonly Pos To;

        public PieceFall(int pieceId, Pos from, Pos to)
        {
            PieceId = pieceId;
            From = from;
            To = to;
        }
    }

    /// <summary>A piece appearing on the board. SpawnRow is the virtual row (>= Height for refills) it animates from.</summary>
    public readonly struct PieceSpawn
    {
        public readonly Piece Piece;
        public readonly Pos Position;
        public readonly int SpawnRow;

        public PieceSpawn(Piece piece, Pos position, int spawnRow)
        {
            Piece = piece;
            Position = position;
            SpawnRow = spawnRow;
        }
    }

    /// <summary>A block (stone, blight or egg) taking a hit.</summary>
    public readonly struct StoneHit
    {
        public readonly Pos Position;
        public readonly int PieceId;
        public readonly int RemainingHp;
        public readonly PieceType Type;

        public StoneHit(Pos position, int pieceId, int remainingHp, PieceType type = PieceType.Stone)
        {
            Position = position;
            PieceId = pieceId;
            RemainingHp = remainingHp;
            Type = type;
        }

        public bool Destroyed => RemainingHp <= 0;
    }

    /// <summary>One wave: clear -> spawn bonuses -> gravity -> refill. Cascade level 0 is the action itself.</summary>
    public sealed class ResolutionStep
    {
        public int CascadeLevel { get; internal set; }

        public List<MatchGroup> Groups { get; } = new List<MatchGroup>();

        public List<ClearedPiece> Cleared { get; } = new List<ClearedPiece>();

        public List<PieceSpawn> SpecialsCreated { get; } = new List<PieceSpawn>();

        public List<Pos> IceBroken { get; } = new List<Pos>();

        public List<StoneHit> StoneHits { get; } = new List<StoneHit>();

        public List<PieceFall> Falls { get; } = new List<PieceFall>();

        public List<PieceSpawn> Refills { get; } = new List<PieceSpawn>();

        /// <summary>Points before cascade / power-up multipliers.</summary>
        public long BasePoints { get; internal set; }

        public int SpecialsActivated { get; internal set; }

        /// <summary>Bonus + bonus swaps fused in this wave (0 or 1).</summary>
        public int CombosTriggered { get; internal set; }

        /// <summary>Cleared gems per color, indexed by (int)PieceColor.</summary>
        public int[] ClearedByColor { get; } = new int[6];

        public int StonesDestroyed => CountDestroyed(PieceType.Stone);

        public int BlightCleared => CountDestroyed(PieceType.Blight);

        public int EggsHatched => CountDestroyed(PieceType.Egg);

        /// <summary>Countdown bombs matched or blasted in this wave.</summary>
        public int BombsDefused { get; internal set; }

        private int CountDestroyed(PieceType type)
        {
            int n = 0;
            foreach (StoneHit hit in StoneHits)
            {
                if (hit.Destroyed && hit.Type == type)
                {
                    n++;
                }
            }
            return n;
        }
    }

    /// <summary>Everything that happened because of one player action.</summary>
    public sealed class ResolutionResult
    {
        public List<ResolutionStep> Steps { get; } = new List<ResolutionStep>();

        /// <summary>Number of automatic cascades after the action's own wave.</summary>
        public int CascadeCount => Math.Max(0, Steps.Count - 1);

        public bool Shuffled { get; internal set; }

        /// <summary>Snapshot after the deadlock shuffle, for views to re-layout pieces by id.</summary>
        public GameBoard BoardAfterShuffle { get; internal set; }

        public bool GoldenChainConsumed { get; internal set; }

        public long TotalBasePoints
        {
            get
            {
                long total = 0;
                foreach (ResolutionStep s in Steps)
                {
                    total += s.BasePoints;
                }
                return total;
            }
        }
    }

    /// <summary>Power-up flags that change how a resolution behaves.</summary>
    public sealed class ResolveOptions
    {
        public static readonly ResolveOptions None = new ResolveOptions();

        /// <summary>Golden Chain: the first match clears its row and column too.</summary>
        public bool GoldenChainArmed { get; set; }

        /// <summary>Bright Spark: every Match-3 also spawns a Line bomb (the GDD's "Match 3 = +1 bonus piece").</summary>
        public bool BrightSparkActive { get; set; }
    }

    /// <summary>
    /// Applies the match-3 rules to a board: removal, bonus creation, chain reactions, stones, ice,
    /// gravity, refills and cascades, then deadlock shuffling. Produces a full event log for animation.
    /// Scoring multipliers are NOT applied here (see Scoring.CascadeCalculator).
    /// </summary>
    public sealed class ResolutionEngine
    {
        private readonly ScoringBalance _scoring;
        private readonly int _maxSteps;
        private readonly RefillSpawner _spawner;
        private readonly DeterministicRandom _shuffleRng;
        private readonly int _colorCount;

        public ResolutionEngine(GameBalance balance, RefillSpawner spawner, DeterministicRandom shuffleRng)
        {
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }
            _scoring = balance.Scoring;
            _maxSteps = balance.Board.MaxResolutionSteps;
            _spawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
            _shuffleRng = shuffleRng ?? throw new ArgumentNullException(nameof(shuffleRng));
            _colorCount = spawner.ColorCount;
        }

        /// <summary>Validates and resolves a player swap. On failure the board is untouched.</summary>
        public OperationResult<ResolutionResult> ResolveSwap(GameBoard board, Pos a, Pos b, ResolveOptions options = null)
        {
            ErrorCode error = MoveFinder.CheckSwap(board, a, b);
            if (error != ErrorCode.None)
            {
                return OperationResult<ResolutionResult>.Fail(error);
            }

            bool combo = MoveFinder.IsSpecialCombo(board, a, b);
            Piece first = board[a];
            Piece second = board[b];
            board.Swap(a, b);
            List<MatchGroup> groups = MatchFinder.FindMatches(board);
            if (!combo)
            {
                return OperationResult<ResolutionResult>.Ok(Run(board, groups, null, ClearCause.PowerUp, a, b, true, options ?? ResolveOptions.None));
            }

            // Bonus + bonus: both pieces fuse where the player dropped the first one, into one bigger blast.
            var comboCells = new List<Pos> { b, a };
            ClearCause cause = ComboCells(board, b, first.Type, second.Type, comboCells);
            var fused = new HashSet<int> { first.Id, second.Id };
            return OperationResult<ResolutionResult>.Ok(Run(board, groups, comboCells, cause, a, b, true, options ?? ResolveOptions.None, fused));
        }

        /// <summary>
        /// Combined effect of two swapped bonuses, centered on <paramref name="center"/>:
        /// line + line = full row and column; line + bomb = three rows and three columns; bomb + bomb = 5x5 blast.
        /// </summary>
        private static ClearCause ComboCells(GameBoard board, Pos center, PieceType first, PieceType second, List<Pos> cells)
        {
            bool firstBomb = first == PieceType.AreaBomb;
            bool secondBomb = second == PieceType.AreaBomb;
            if (firstBomb && secondBomb)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        cells.Add(new Pos(center.X + dx, center.Y + dy));
                    }
                }
                return ClearCause.AreaBlast;
            }

            int half = firstBomb || secondBomb ? 1 : 0;
            for (int offset = -half; offset <= half; offset++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    cells.Add(new Pos(x, center.Y + offset));
                }
                for (int y = 0; y < board.Height; y++)
                {
                    cells.Add(new Pos(center.X + offset, y));
                }
            }
            return ClearCause.LineBlast;
        }

        /// <summary>Clears arbitrary cells (power-ups), then lets cascades play out.</summary>
        public ResolutionResult ResolveForcedClear(GameBoard board, IReadOnlyList<Pos> cells, ClearCause cause, ResolveOptions options = null)
        {
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }
            return Run(board, new List<MatchGroup>(), cells, cause, default, default, false, options ?? ResolveOptions.None);
        }

        private ResolutionResult Run(
            GameBoard board,
            List<MatchGroup> groups,
            IReadOnlyList<Pos> forced,
            ClearCause forcedCause,
            Pos swapA,
            Pos swapB,
            bool hasSwap,
            ResolveOptions options,
            HashSet<int> fused = null)
        {
            var result = new ResolutionResult();
            int level = 0;

            while (groups.Count > 0 || (forced != null && forced.Count > 0))
            {
                ResolutionStep step = ExecuteStep(board, level, groups, forced, forcedCause, swapA, swapB, hasSwap, options, result, fused);
                fused = null;
                result.Steps.Add(step);

                level++;
                forced = null;
                hasSwap = false;
                if (level >= _maxSteps)
                {
                    break;
                }
                groups = MatchFinder.FindMatches(board);
            }

            if (MatchFinder.HasAnyMatch(board) || !MoveFinder.HasValidMove(board))
            {
                if (BoardShuffler.EnsurePlayable(board, _shuffleRng, _colorCount))
                {
                    result.Shuffled = true;
                    result.BoardAfterShuffle = board.Clone();
                }
            }

            return result;
        }

        private ResolutionStep ExecuteStep(
            GameBoard board,
            int level,
            List<MatchGroup> groups,
            IReadOnlyList<Pos> forced,
            ClearCause forcedCause,
            Pos swapA,
            Pos swapB,
            bool hasSwap,
            ResolveOptions options,
            ResolutionResult result,
            HashSet<int> fused)
        {
            var step = new ResolutionStep { CascadeLevel = level };
            step.Groups.AddRange(groups);

            var causes = new Dictionary<Pos, ClearCause>();
            var order = new List<Pos>();
            var specials = new List<PieceSpawn>();
            var reserved = new HashSet<Pos>();
            long points = 0;

            void Mark(Pos p, ClearCause cause)
            {
                if (!board.InBounds(p) || board[p].IsEmpty || causes.ContainsKey(p))
                {
                    return;
                }
                causes[p] = cause;
                order.Add(p);
            }

            void AddSpecial(Pos p, PieceColor color, PieceType type)
            {
                if (reserved.Add(p))
                {
                    specials.Add(new PieceSpawn(new Piece(1, color, type), p, p.Y));
                }
            }

            // 1) Matches and the bonuses they create.
            foreach (MatchGroup group in groups)
            {
                foreach (Pos cell in group.Cells)
                {
                    Mark(cell, ClearCause.Match);
                }
                points += (long)group.Cells.Count * _scoring.PointsPerPiece;

                Pos spawn = ChooseSpawn(group, swapA, swapB, hasSwap);
                MatchRun longest = LongestRun(group);
                switch (group.Shape)
                {
                    case MatchShape.Line4:
                        points += _scoring.Line4Bonus;
                        AddSpecial(spawn, group.Color, longest.Horizontal ? PieceType.LineHorizontal : PieceType.LineVertical);
                        break;
                    case MatchShape.Cross:
                        points += _scoring.CrossBonus;
                        AddSpecial(spawn, group.Color, PieceType.AreaBomb);
                        break;
                    case MatchShape.Line5:
                        points += _scoring.Line5SuperBonus;
                        foreach (Pos p in board.AllPositions())
                        {
                            Piece piece = board[p];
                            if (piece.IsMatchable && piece.Color == group.Color)
                            {
                                Mark(p, ClearCause.ColorBlast);
                            }
                        }
                        break;
                    default:
                        if (options.BrightSparkActive)
                        {
                            AddSpecial(spawn, group.Color, longest.Horizontal ? PieceType.LineHorizontal : PieceType.LineVertical);
                        }
                        break;
                }

                if (options.GoldenChainArmed && !result.GoldenChainConsumed)
                {
                    result.GoldenChainConsumed = true;
                    for (int x = 0; x < board.Width; x++)
                    {
                        Mark(new Pos(x, spawn.Y), ClearCause.PowerUp);
                    }
                    for (int y = 0; y < board.Height; y++)
                    {
                        Mark(new Pos(spawn.X, y), ClearCause.PowerUp);
                    }
                }
            }

            // 2) Cells forced by a power-up.
            if (forced != null)
            {
                foreach (Pos p in forced)
                {
                    Mark(p, forcedCause);
                }
            }

            // 3) Chain reactions: any bonus caught in the clear set detonates (order-stable BFS).
            var detonated = new HashSet<int>();
            if (fused != null)
            {
                // The two combined bonuses already gave their (bigger) effect through the forced cells.
                foreach (int id in fused)
                {
                    detonated.Add(id);
                    step.SpecialsActivated++;
                    points += _scoring.SpecialActivationBonus;
                }
                step.CombosTriggered++;
            }
            for (int i = 0; i < order.Count; i++)
            {
                Pos p = order[i];
                Piece piece = board[p];
                if (!piece.IsSpecial || !detonated.Add(piece.Id))
                {
                    continue;
                }

                step.SpecialsActivated++;
                points += _scoring.SpecialActivationBonus;
                switch (piece.Type)
                {
                    case PieceType.LineHorizontal:
                        for (int x = 0; x < board.Width; x++)
                        {
                            Mark(new Pos(x, p.Y), ClearCause.LineBlast);
                        }
                        break;
                    case PieceType.LineVertical:
                        for (int y = 0; y < board.Height; y++)
                        {
                            Mark(new Pos(p.X, y), ClearCause.LineBlast);
                        }
                        break;
                    case PieceType.AreaBomb:
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                Mark(new Pos(p.X + dx, p.Y + dy), ClearCause.AreaBlast);
                            }
                        }
                        break;
                }
            }

            // 4) Stones: hit when blasted directly, or when a MATCH happens next to them (once per stone per wave).
            var stoneOrder = new List<Pos>();
            var stoneSet = new HashSet<Pos>();
            foreach (Pos p in order)
            {
                if (board[p].IsBlock && stoneSet.Add(p))
                {
                    stoneOrder.Add(p);
                }
            }
            foreach (Pos p in order)
            {
                if (causes[p] != ClearCause.Match || board[p].IsBlock)
                {
                    continue;
                }
                AddAdjacentStone(board, p.Offset(-1, 0), stoneOrder, stoneSet);
                AddAdjacentStone(board, p.Offset(1, 0), stoneOrder, stoneSet);
                AddAdjacentStone(board, p.Offset(0, -1), stoneOrder, stoneSet);
                AddAdjacentStone(board, p.Offset(0, 1), stoneOrder, stoneSet);
            }

            // 5) Remove gems, break ice.
            foreach (Pos p in order)
            {
                Piece piece = board[p];
                if (piece.IsBlock)
                {
                    continue;
                }
                if (piece.IsTimeBomb)
                {
                    step.BombsDefused++;
                    points += _scoring.SpecialActivationBonus;
                }

                ClearCause cause = causes[p];
                step.Cleared.Add(new ClearedPiece(p, piece, cause));
                step.ClearedByColor[(int)piece.Color]++;
                if (cause != ClearCause.Match)
                {
                    points += _scoring.PointsPerPiece;
                }
                if (board.ReduceIce(p))
                {
                    step.IceBroken.Add(p);
                    points += _scoring.IceLayerPoints;
                }
                board[p] = Piece.Empty;
            }

            var hatched = new List<(Pos Position, int EggId)>();
            foreach (Pos p in stoneOrder)
            {
                Piece stone = board[p];
                int hp = stone.Hp - 1;
                step.StoneHits.Add(new StoneHit(p, stone.Id, hp, stone.Type));
                if (hp <= 0)
                {
                    board[p] = Piece.Empty;
                    points += _scoring.StonePoints;
                    if (stone.IsEgg)
                    {
                        hatched.Add((p, stone.Id));
                    }
                }
                else
                {
                    board[p] = stone.WithHp((byte)hp);
                }
            }

            // 6) Place newly created bonuses (their cells were just cleared).
            foreach (PieceSpawn template in specials)
            {
                if (!board[template.Position].IsEmpty)
                {
                    continue;
                }
                Piece created = board.CreatePiece(template.Piece.Color, template.Piece.Type);
                board[template.Position] = created;
                step.SpecialsCreated.Add(new PieceSpawn(created, template.Position, template.Position.Y));
            }

            // 6b) Hatched dragon eggs leave a bonus gem; its kind and color derive from the egg id (deterministic, no RNG).
            PieceType[] hatchTypes = { PieceType.LineHorizontal, PieceType.AreaBomb, PieceType.LineVertical };
            foreach ((Pos position, int eggId) in hatched)
            {
                if (!board[position].IsEmpty)
                {
                    continue;
                }
                Piece hatchling = board.CreatePiece((PieceColor)(eggId % _colorCount), hatchTypes[eggId % hatchTypes.Length]);
                board[position] = hatchling;
                step.SpecialsCreated.Add(new PieceSpawn(hatchling, position, position.Y));
            }

            // 7) Gravity + refill, column by column.
            ApplyGravityAndRefill(board, step);

            step.BasePoints = points;
            return step;
        }

        private void ApplyGravityAndRefill(GameBoard board, ResolutionStep step)
        {
            for (int x = 0; x < board.Width; x++)
            {
                int write = 0;
                for (int y = 0; y < board.Height; y++)
                {
                    Piece piece = board[x, y];
                    if (piece.IsEmpty)
                    {
                        continue;
                    }
                    if (y != write)
                    {
                        board[x, write] = piece;
                        board[x, y] = Piece.Empty;
                        step.Falls.Add(new PieceFall(piece.Id, new Pos(x, y), new Pos(x, write)));
                    }
                    write++;
                }

                for (int y = write; y < board.Height; y++)
                {
                    Piece fresh = board.CreatePiece(_spawner.NextColor(x));
                    board[x, y] = fresh;
                    step.Refills.Add(new PieceSpawn(fresh, new Pos(x, y), board.Height + (y - write)));
                }
            }
        }

        private static void AddAdjacentStone(GameBoard board, Pos p, List<Pos> order, HashSet<Pos> set)
        {
            if (board.InBounds(p) && board[p].IsBlock && set.Add(p))
            {
                order.Add(p);
            }
        }

        /// <summary>
        /// Where a bonus appears: on the swapped cell when the player caused the match (the natural feel),
        /// else on the crossing cell of an L/T, else in the middle of the longest run.
        /// </summary>
        private static Pos ChooseSpawn(MatchGroup group, Pos swapA, Pos swapB, bool hasSwap)
        {
            if (hasSwap)
            {
                if (group.Contains(swapA))
                {
                    return swapA;
                }
                if (group.Contains(swapB))
                {
                    return swapB;
                }
            }

            if (group.Shape == MatchShape.Cross)
            {
                var horizontalCells = new HashSet<Pos>();
                foreach (MatchRun run in group.Runs)
                {
                    if (run.Horizontal)
                    {
                        foreach (Pos c in run.Cells)
                        {
                            horizontalCells.Add(c);
                        }
                    }
                }
                foreach (MatchRun run in group.Runs)
                {
                    if (run.Horizontal)
                    {
                        continue;
                    }
                    foreach (Pos c in run.Cells)
                    {
                        if (horizontalCells.Contains(c))
                        {
                            return c;
                        }
                    }
                }
            }

            MatchRun longest = LongestRun(group);
            return longest.Cells[(longest.Length - 1) / 2];
        }

        private static MatchRun LongestRun(MatchGroup group)
        {
            MatchRun best = group.Runs[0];
            foreach (MatchRun run in group.Runs)
            {
                if (run.Length > best.Length)
                {
                    best = run;
                }
            }
            return best;
        }
    }
}
