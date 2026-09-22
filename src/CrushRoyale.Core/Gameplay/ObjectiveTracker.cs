using System;
using System.Collections.Generic;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Core.Gameplay
{
    public sealed class ObjectiveProgress
    {
        internal ObjectiveProgress(StageObjective objective, long target)
        {
            Objective = objective;
            Target = Math.Max(1, target);
        }

        public StageObjective Objective { get; }

        public long Target { get; }

        public long Current { get; internal set; }

        public bool IsComplete => Current >= Target;

        /// <summary>0..1 for progress bars.</summary>
        public float Fraction => Math.Min(1f, Current / (float)Target);
    }

    /// <summary>Tracks stage win conditions from resolution events.</summary>
    public sealed class ObjectiveTracker
    {
        private readonly List<ObjectiveProgress> _progress = new List<ObjectiveProgress>();

        public ObjectiveTracker(IEnumerable<StageObjective> objectives, GameBoard initialBoard, long bossHp)
        {
            if (initialBoard == null)
            {
                throw new ArgumentNullException(nameof(initialBoard));
            }
            if (objectives == null)
            {
                return;
            }

            foreach (StageObjective objective in objectives)
            {
                long target;
                switch (objective.Type)
                {
                    case ObjectiveType.ClearIce:
                        target = objective.Target > 0 ? Math.Min(objective.Target, initialBoard.TotalIceLayers) : initialBoard.TotalIceLayers;
                        break;
                    case ObjectiveType.BreakStones:
                        target = objective.Target > 0 ? objective.Target : initialBoard.CountStones();
                        break;
                    case ObjectiveType.DefeatBoss:
                        target = bossHp > 0 ? bossHp : objective.Target;
                        break;
                    default:
                        target = objective.Target;
                        break;
                }
                _progress.Add(new ObjectiveProgress(objective, target));
            }
        }

        public IReadOnlyList<ObjectiveProgress> Progress => _progress;

        public bool HasObjectives => _progress.Count > 0;

        public bool AllComplete
        {
            get
            {
                if (_progress.Count == 0)
                {
                    return false;
                }
                foreach (ObjectiveProgress p in _progress)
                {
                    if (!p.IsComplete)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public void ApplyStep(ResolutionStep step)
        {
            foreach (ObjectiveProgress p in _progress)
            {
                switch (p.Objective.Type)
                {
                    case ObjectiveType.ClearIce:
                        p.Current += step.IceBroken.Count;
                        break;
                    case ObjectiveType.CollectColor:
                        if (p.Objective.Color != PieceColor.None)
                        {
                            p.Current += step.ClearedByColor[(int)p.Objective.Color];
                        }
                        break;
                    case ObjectiveType.BreakStones:
                        p.Current += step.StonesDestroyed;
                        break;
                    case ObjectiveType.DestroyBlight:
                        p.Current += step.BlightCleared;
                        break;
                    case ObjectiveType.BreakChains:
                        p.Current += step.ChainsBroken.Count;
                        break;
                }
            }
        }

        public void SetScore(long score)
        {
            foreach (ObjectiveProgress p in _progress)
            {
                if (p.Objective.Type == ObjectiveType.ReachScore || p.Objective.Type == ObjectiveType.DefeatBoss)
                {
                    p.Current = score;
                }
            }
        }
    }
}
