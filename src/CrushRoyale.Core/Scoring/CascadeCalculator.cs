using System;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Scoring
{
    public enum CascadeAnimationKind : byte
    {
        Cascade = 0,
        MegaCascade = 1,
        RedSurgeStarted = 2,
        RedSurgeEnded = 3
    }

    /// <summary>Signal for the presentation layer (camera shake, "MEGA CASCADE" banner, surge music...).</summary>
    public readonly struct CascadeAnimationTrigger
    {
        public readonly CascadeAnimationKind Kind;
        public readonly int CascadeLevel;
        public readonly int TimestampMs;

        public CascadeAnimationTrigger(CascadeAnimationKind kind, int cascadeLevel, int timestampMs)
        {
            Kind = kind;
            CascadeLevel = cascadeLevel;
            TimestampMs = timestampMs;
        }
    }

    /// <summary>
    /// Task 2: cascade scoring + Red Surge combo meter.
    ///
    /// Per action:
    ///   stepPoints   = step.BasePoints * cascadeMultiplier(level)      (L0/L1 x1, L2 x1.5, L3 x2, L4+ x3)
    ///   actionPoints = sum(stepPoints) * (1 + 10% * cascades)         (GDD "each cascade adds +10%", capped)
    /// then the session applies time-based multipliers (Red Surge x2, power-ups) with a global cap.
    ///
    /// All arithmetic is integer permille; the float methods exist only for UI display.
    /// </summary>
    public sealed class CascadeCalculator
    {
        private readonly ScoringBalance _scoring;
        private readonly ComboMeterBalance _combo;
        private int _currentLevel;
        private long _sequencePoints;
        private int _lastKnownTimeMs;
        private bool _surgeEndAnnounced = true;

        public CascadeCalculator(GameBalance balance)
        {
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }
            _scoring = balance.Scoring;
            _combo = balance.ComboMeter;
        }

        public event Action<CascadeAnimationTrigger> AnimationTriggered;

        /// <summary>0..MaxPermille (1000 = 100%).</summary>
        public int ComboMeterPermille { get; private set; }

        public int SurgeStartMs { get; private set; } = -1;

        public int SurgeEndMs { get; private set; } = -1;

        public int TotalCascades { get; private set; }

        public int MegaCascades { get; private set; }

        public int MaxCascadeLevel { get; private set; }

        public int SurgeActivations { get; private set; }

        /// <summary>Starts scoring a new action (resets the cascade level).</summary>
        public void BeginSequence(int nowMs)
        {
            _currentLevel = 0;
            _sequencePoints = 0;
            _lastKnownTimeMs = nowMs;
        }

        /// <summary>Cascade level of the last scored step (0 = the player's own match).</summary>
        public int CalculateCascadeLevel() => _currentLevel;

        public float GetCascadeMultiplier(int cascadeLevel) => _scoring.GetCascadeMultiplierPermille(cascadeLevel) / 1000f;

        /// <summary>Prompt API: baseScore * multiplier for that level (display helper).</summary>
        public float CalculateCascadeScore(float baseScore, int cascadeLevel)
        {
            if (baseScore < 0f || float.IsNaN(baseScore))
            {
                throw new ArgumentOutOfRangeException(nameof(baseScore));
            }
            return baseScore * GetCascadeMultiplier(cascadeLevel);
        }

        /// <summary>
        /// Scores one resolution step and accumulates it into the current sequence.
        /// <paramref name="cascadeBoostPermille"/> is 2000 while Cascade Infinity is active (doubles cascade multipliers only).
        /// </summary>
        public long ScoreStep(ResolutionStep step, int cascadeBoostPermille = 1000)
        {
            if (step == null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            int level = step.CascadeLevel;
            _currentLevel = level;

            long multiplier = _scoring.GetCascadeMultiplierPermille(level);
            if (level >= 1 && cascadeBoostPermille > 1000)
            {
                multiplier = multiplier * cascadeBoostPermille / 1000;
            }

            long points = step.BasePoints * multiplier / 1000;
            _sequencePoints += points;

            if (level >= 1)
            {
                TotalCascades++;
                if (level > MaxCascadeLevel)
                {
                    MaxCascadeLevel = level;
                }

                bool mega = level >= _scoring.MegaCascadeLevel;
                if (mega)
                {
                    MegaCascades++;
                }
                AnimationTriggered?.Invoke(new CascadeAnimationTrigger(mega ? CascadeAnimationKind.MegaCascade : CascadeAnimationKind.Cascade, level, _lastKnownTimeMs));
            }

            return points;
        }

        public int GetChainBonusPermille(int cascadeCount)
        {
            if (cascadeCount <= 0)
            {
                return 0;
            }
            return (int)Math.Min(_scoring.MaxChainBonusPermille, (long)cascadeCount * _scoring.ChainBonusPerCascadePermille);
        }

        /// <summary>Closes the sequence: applies the stacking chain bonus and returns the action's points (before time multipliers).</summary>
        public long FinishSequence(int cascadeCount)
        {
            long total = _sequencePoints * (1000 + GetChainBonusPermille(cascadeCount)) / 1000;
            _sequencePoints = 0;
            return total;
        }

        /// <summary>Meter fill produced by one step: by match shape, plus the cascade fill for automatic waves.</summary>
        public int GetComboFill(ResolutionStep step)
        {
            if (step == null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            int fill = 0;
            foreach (MatchGroup group in step.Groups)
            {
                switch (group.Shape)
                {
                    case MatchShape.Line3: fill += _combo.Line3Fill; break;
                    case MatchShape.Line4: fill += _combo.Line4Fill; break;
                    case MatchShape.Cross: fill += _combo.CrossFill; break;
                    case MatchShape.Line5: fill += _combo.Line5Fill; break;
                }
            }
            if (step.CascadeLevel >= 1)
            {
                fill += _combo.CascadeFill;
            }
            return fill;
        }

        public bool IsRedSurgeActive(int nowMs) => SurgeStartMs >= 0 && nowMs >= SurgeStartMs && nowMs < SurgeEndMs;

        /// <summary>
        /// Adds fill (permille). When the meter is full, Red Surge starts right after this action and lasts
        /// SurgeDurationMs; the meter is reset and cannot fill while the surge runs. Returns true on activation.
        /// </summary>
        public bool AddComboFill(int permille, int nowMs)
        {
            _lastKnownTimeMs = nowMs;
            if (permille <= 0 || IsRedSurgeActive(nowMs) || SurgeStartMs > nowMs)
            {
                return false;
            }

            ComboMeterPermille = Math.Min(_combo.MaxPermille, ComboMeterPermille + permille);
            if (ComboMeterPermille < _combo.MaxPermille)
            {
                return false;
            }

            ComboMeterPermille = 0;
            SurgeStartMs = nowMs + 1;
            SurgeEndMs = SurgeStartMs + _combo.SurgeDurationMs;
            SurgeActivations++;
            _surgeEndAnnounced = false;
            AnimationTriggered?.Invoke(new CascadeAnimationTrigger(CascadeAnimationKind.RedSurgeStarted, 0, nowMs));
            return true;
        }

        /// <summary>Prompt API: fill in percentage points (e.g. 10 for +10%). Returns true if Red Surge activated.</summary>
        public bool UpdateComboMeter(float fillAmount)
        {
            if (float.IsNaN(fillAmount) || fillAmount < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fillAmount));
            }
            return AddComboFill((int)Math.Round(fillAmount * 10f), _lastKnownTimeMs);
        }

        public int GetComboMultiplierPermille(int nowMs) => IsRedSurgeActive(nowMs) ? _combo.SurgeMultiplierPermille : 1000;

        /// <summary>Prompt API: current Red Surge multiplier (2.0 while active, else 1.0).</summary>
        public float GetComboMultiplier() => GetComboMultiplierPermille(_lastKnownTimeMs) / 1000f;

        /// <summary>Advances the clock (announces the end of Red Surge).</summary>
        public void Update(int nowMs)
        {
            if (nowMs > _lastKnownTimeMs)
            {
                _lastKnownTimeMs = nowMs;
            }
            if (!_surgeEndAnnounced && SurgeEndMs >= 0 && nowMs >= SurgeEndMs)
            {
                _surgeEndAnnounced = true;
                AnimationTriggered?.Invoke(new CascadeAnimationTrigger(CascadeAnimationKind.RedSurgeEnded, 0, SurgeEndMs));
            }
        }
    }
}
