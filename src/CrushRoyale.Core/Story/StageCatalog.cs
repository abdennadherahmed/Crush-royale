using System;
using System.Collections.Generic;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Story
{
    /// <summary>
    /// Deterministic generator of the 1000 campaign stages (+ endless post-game stages).
    /// Structure: 5 acts x 10 chapters x 20 stages; mini-boss at stage 10 and boss at stage 20 of every chapter.
    /// Difficulty ramps quickly over the first chapters then flattens (see DifficultyKnots), with a small relief right after each chapter boss.
    /// Stage objectives rotate (score, collect, ice, stones) so 1000 stages don't feel identical.
    /// </summary>
    public sealed class StageCatalog
    {
        /// <summary>Endless stages reuse the layout rhythm of the last act with new seeds.</summary>
        public const int EndlessCycle = 200;

        private const ulong StageSeedSalt = 0xC4A57A6E;

        /// <summary>Stages 1-3 ask for 45% of the normal score.</summary>
        private const int OnboardingStages = 3;

        private const int OnboardingTargetPermille = 450;

        private const int StoneReliefPermille = 50;

        private const int IceReliefPermille = 20;

        private const int ObstacleReliefCapPermille = 450;

        private const int BossHpPermille = 1100;

        /// <summary>Goals are capped to these shares of the expert bot's reach (StageTuning.g.cs).</summary>
        /// <summary>Stages 1 to 40 ease in; the first asks 46% of what its difficulty would otherwise demand.</summary>
        /// <summary>A stage may ask for this much of what a mid run reaches: above 100% it is a coin toss, below it is free.</summary>
        private const int MidReachCeilingPermille = 1000;

        /// <summary>
        /// What a super hard stage may ask for, as a share of what a mid player reaches with no boosts equipped.
        ///
        /// Above 1000 the stage cannot be won on skill alone at mid level: the missing fifth has to come from the
        /// loadout. Three boosts are worth roughly a quarter to a third of a run, so 1200 is a stage you lose once,
        /// come back to with a Chrono Bomb and a Multiplier, and win -- which is the point. It is deliberately not
        /// higher: a wall nobody can climb is not difficulty, it is a dead end.
        /// </summary>
        private const int BoostGateCeilingPermille = 1850;

        /// <summary>
        /// First stage allowed to be boost-gated.
        ///
        /// The opening chapter has to be winnable with nothing, by someone who has never played a match-3. The gates
        /// start at the second super hard stage, so the first one (17) still teaches what "super hard" means without
        /// demanding an inventory.
        /// </summary>
        private const int BoostGateFromStage = 30;

        /// <summary>
        /// How far above a perfect bare-handed run a gated target may sit.
        ///
        /// Above 1000 the stage cannot be won without a loadout at all -- which is the whole point of the gate, and
        /// the reason the audit stops treating these stages as broken. The margin is small on purpose: a single
        /// Chrono Bomb or Multiplier closes it, so the stage falls on the second attempt with the right boost, not
        /// on the tenth with luck.
        /// </summary>
        private const int BoostGatePerfectCapPermille = 1100;

        /// <summary>Share of a perfect run's gems, ice or stones a gated stage demands. Over 1000 by design.</summary>
        private const int BoostGateObstaclePermille = 1150;

        private const int OnboardingEase = 41;

        private const int OnboardingFloorPermille = 460;

        private const int TunedScoreEasyPermille = 780;

        private const int TunedScoreHardPermille = 800;

        private const int TunedBossReliefPermille = 130;

        /// <summary>Sawtooth: extra share of the expert's reach on Hard / Super hard stages, relief on the stage after.</summary>
        private const int TunedHardExtraPermille = 90;

        private const int TunedSuperHardExtraPermille = 110;

        private const int TunedBreatherPermille = 80;

        private const int TunedMaxSharePermille = 870;

        private const int TunedObstacleEasyPermille = 820;

        private const int TunedObstacleHardPermille = 880;

        /// <summary>
        /// Relief on "collect N of a colour" goals. The audit bots always play the highest-scoring move and cannot
        /// steer a colour at all, so those stages were twice as likely as any other to turn into a wall: 257, 277,
        /// 297, 907 and 917 were unwinnable for an average run even with the difficulty assist.
        /// </summary>
        private const int TunedCollectReliefPermille = 260;

        /// <summary>Share of the ice layers / stones an obstacle goal asks for.</summary>
        private const int GoalObstaclePermille = 900;

        private readonly GameBalance _balance;
        private readonly Dictionary<int, StageData> _cache = new Dictionary<int, StageData>();
        private readonly object _lock = new object();
        private readonly bool _applyTuning;

        public StageCatalog(GameBalance balance) : this(balance, true)
        {
        }

        /// <summary>Catalog; <paramref name="applyTuning"/> is false only for tools/StageAudit when it measures what bots can reach.</summary>
        public StageCatalog(GameBalance balance, bool applyTuning)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _applyTuning = applyTuning;
        }

        public int TotalCampaignStages => _balance.Story.TotalStages;

        public StageData Get(int stageId)
        {
            if (stageId < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(stageId), "Stages start at 1.");
            }
            lock (_lock)
            {
                if (!_cache.TryGetValue(stageId, out StageData stage))
                {
                    stage = Build(stageId);
                    _cache[stageId] = stage;
                }
                return stage;
            }
        }

        public IEnumerable<StageData> GetRange(int firstId, int lastId)
        {
            for (int id = firstId; id <= lastId; id++)
            {
                yield return Get(id);
            }
        }

        /// <summary>Act (1-5), chapter (1-50) and index in chapter (1-20).</summary>
        public void Locate(int stageId, out int act, out int chapter, out int indexInChapter)
        {
            StoryBalance s = _balance.Story;
            int campaignId = CampaignEquivalent(stageId);
            int perAct = s.ChaptersPerAct * s.StagesPerChapter;
            act = (campaignId - 1) / perAct + 1;
            chapter = (campaignId - 1) / s.StagesPerChapter + 1;
            indexInChapter = (campaignId - 1) % s.StagesPerChapter + 1;
        }

        /// <summary>
        /// Difficulty curve knots (stage, permille): a fast ramp over the first chapters so progress is felt early,
        /// flattening towards the end of the campaign. Integer interpolation keeps client and server identical.
        /// </summary>
        private static readonly int[,] DifficultyKnots =
        {
            { 1, 0 }, { 20, 120 }, { 50, 250 }, { 100, 400 }, { 200, 560 }, { 400, 750 }, { 700, 900 }, { 1000, 1000 }
        };

        /// <summary>Difficulty permille: 0 at stage 1, fast early ramp (see knots), -3% relief on the 3 stages after a boss.</summary>
        public int DifficultyForStage(int stageId)
        {
            StoryBalance s = _balance.Story;
            if (stageId <= 1)
            {
                return 0;
            }
            if (stageId > s.TotalStages)
            {
                return 1000;
            }

            // Knots are expressed for a 1000-stage campaign; other lengths are scaled.
            long scaled = (long)stageId * 1000 / s.TotalStages;
            int raw = 1000;
            for (int i = 1; i < DifficultyKnots.GetLength(0); i++)
            {
                int x1 = DifficultyKnots[i, 0];
                if (scaled <= x1)
                {
                    int x0 = DifficultyKnots[i - 1, 0];
                    int y0 = DifficultyKnots[i - 1, 1];
                    int y1 = DifficultyKnots[i, 1];
                    raw = y0 + (int)((scaled - x0) * (y1 - y0) / Math.Max(1, x1 - x0));
                    break;
                }
            }

            int index = (stageId - 1) % s.StagesPerChapter + 1;
            int chapter = (stageId - 1) / s.StagesPerChapter + 1;
            if (chapter > 1 && index <= 3)
            {
                raw -= 30;
            }
            return Math.Max(0, Math.Min(1000, raw));
        }

        private int CampaignEquivalent(int stageId)
        {
            int total = _balance.Story.TotalStages;
            if (stageId <= total)
            {
                return stageId;
            }
            int cycle = Math.Min(EndlessCycle, total);
            return total - cycle + 1 + (stageId - total - 1) % cycle;
        }

        private StageData Build(int id)
        {
            StoryBalance s = _balance.Story;
            bool endless = id > s.TotalStages;
            Locate(id, out int act, out int chapter, out int index);
            int d = DifficultyForStage(id);
            var rng = DeterministicRandom.Derive(StageSeedSalt, (ulong)id);

            var stage = new StageData
            {
                Id = id,
                Act = act,
                Chapter = endless ? s.Acts * s.ChaptersPerAct + (id - s.TotalStages - 1) / s.StagesPerChapter + 1 : chapter,
                IndexInChapter = index,
                Kingdom = (Kingdom)((act - 1) % 5),
                DifficultyPermille = d,
                ColorCount = id <= 10 ? 5 : 6,
                Seed = StableHash.Mix(StageSeedSalt, (ulong)id),
                IsEndless = endless,
                CharacterId = StoryDatabase.FeaturedCharacterFor(Math.Min(id, s.TotalStages))
            };

            stage.BossKind = BossKindFor(id, act, chapter, index, endless);
            int campaignIndex = (CampaignEquivalent(id) - 1) % s.StagesPerChapter + 1;
            stage.Tier = TierFor(CampaignEquivalent(id), campaignIndex, stage.IsBoss);
            stage.Timed = IsTimed(CampaignEquivalent(id), campaignIndex, stage.IsBoss);

            // Goals are sized from a move budget; timed stages then trade the moves for a clock.
            int moves = Lerp(s.EasyMoveLimit, s.HardMoveLimit, d) + (stage.IsBoss ? s.BossExtraMoves : 0);
            stage.MoveLimit = moves;
            stage.TimeLimitMs = s.MovesStageTimeCapMs;
            if (stage.Timed)
            {
                stage.MoveLimit = 0;
                stage.TimeLimitMs = Lerp(s.TimedEasyTimeLimitMs, s.TimedHardTimeLimitMs, d);
            }

            int factor = Lerp(s.EasyTargetFactorPermille, s.HardTargetFactorPermille, d);
            int target = (int)((long)moves * s.ExpectedPointsPerMove * factor / 1000);
            if (!endless && id <= OnboardingStages)
            {
                // First minutes: the opening stages are near-certain wins so new players feel strong right away.
                target = target * OnboardingTargetPermille / 1000;
            }
            target = RoundTo(Math.Max(200, target), 50);

            // Obstacles unlock gradually: stones from stage 21, ice from stage 41.
            int campaignId = CampaignEquivalent(id);
            int stones = campaignId >= 21 && index % 3 != 1 ? 1 + d * 6 / 1000 : 0;
            int iceCells = campaignId >= 41 && index % 4 == 3 ? 4 + d * 12 / 1000 : 0;
            int iceLayers = 1 + (d >= 600 ? 1 : 0) + (d >= 900 ? 1 : 0);

            // Obstacles eat moves: score goals shrink with them (validated by tools/StageAudit on all 1000 stages).
            int relief = Math.Min(ObstacleReliefCapPermille, stones * StoneReliefPermille + iceCells * iceLayers * IceReliefPermille);
            target = RoundTo(Math.Max(200, target * (1000 - relief) / 1000), 50);

            if (stage.IsBoss)
            {
                stage.BossPhases = BossPhasesFor(stage.BossKind);
                stage.BossHp = RoundTo((int)((long)target * BossHpPermille / 1000), 50);
                stage.BossStonesPerPhase = 1 + (int)stage.BossKind;
                stage.TargetScore = stage.BossHp;
                stage.Objectives.Add(new StageObjective { Type = ObjectiveType.DefeatBoss, Target = stage.BossHp });
                stones = Math.Min(stones, 3);
            }
            else
            {
                switch (index % 5)
                {
                    case 2 when campaignId >= 11:
                        stage.TargetScore = RoundTo(target / 2, 50);
                        stage.Objectives.Add(CollectObjective(stage, rng));
                        break;
                    case 3 when campaignId >= 41:
                        // Ice goals: at most 2 layers, and most of the layers must break (corners stay reachable).
                        iceCells = Math.Max(iceCells, 9 + d * 14 / 1000);
                        iceLayers = Math.Min(iceLayers, 2);
                        stage.TargetScore = RoundTo(target / 2, 50);
                        stage.Objectives.Add(new StageObjective { Type = ObjectiveType.ClearIce, Target = Math.Max(1, (Math.Min(iceCells, 24) * iceLayers * GoalObstaclePermille + 999) / 1000) });
                        break;
                    case 4 when campaignId >= 21:
                        // Six to fourteen stones, not three: "break 2 stones" was a goal the board cleared by itself.
                        stones = Math.Max(stones, 6 + d * 8 / 1000);
                        stage.TargetScore = RoundTo(target / 2, 50);
                        stage.Objectives.Add(new StageObjective { Type = ObjectiveType.BreakStones, Target = Math.Max(1, (Math.Min(stones, 12) * GoalObstaclePermille + 999) / 1000) });
                        break;
                    case 0 when campaignId >= 11:
                        stage.TargetScore = RoundTo(target * 7 / 10, 50);
                        stage.Objectives.Add(new StageObjective { Type = ObjectiveType.ReachScore, Target = stage.TargetScore });
                        stage.Objectives.Add(CollectObjective(stage, rng, 700));
                        break;
                    default:
                        stage.TargetScore = target;
                        stage.Objectives.Add(new StageObjective { Type = ObjectiveType.ReachScore, Target = target });
                        break;
                }
            }

            if (!endless && _applyTuning)
            {
                ApplyTuning(stage, id);
            }

            stage.StoneCount = Math.Min(stones, 12);
            stage.StoneHp = d >= 700 ? 2 : 1;
            stage.IceCells = Math.Min(iceCells, 24);
            stage.IceLayers = iceLayers;
            AddMechanics(stage, campaignId, index, d);

            int starBase = Math.Max(stage.TargetScore, 200);
            stage.TwoStarScore = RoundTo((int)((long)starBase * s.TwoStarPermille / 1000), 50);
            stage.ThreeStarScore = RoundTo((int)((long)starBase * s.ThreeStarPermille / 1000), 50);

            int coins = Lerp(s.BaseStageCoins, s.MaxDifficultyStageCoins, d);
            if (stage.IsBoss)
            {
                coins = coins * s.BossCoinMultiplierPermille / 1000;
            }
            if (stage.Tier == StageTier.Hard)
            {
                coins = coins * s.HardCoinMultiplierPermille / 1000;
            }
            else if (stage.Tier == StageTier.SuperHard)
            {
                coins = coins * s.SuperHardCoinMultiplierPermille / 1000;
            }
            stage.RewardCoins = coins;
            stage.RewardOrbes = (!endless && id % s.OrbeMilestoneInterval == 0 ? s.OrbeMilestoneReward : 0)
                + (stage.Tier == StageTier.SuperHard ? s.SuperHardOrbes : 0);

            if (!endless)
            {
                if (index == 1)
                {
                    stage.DialogueBeforeId = "dlg.ch" + chapter + ".intro";
                }
                if (index == s.MiniBossIndex)
                {
                    stage.DialogueAfterId = "dlg.ch" + chapter + ".mid";
                }
                if (index == s.FinalBossIndex)
                {
                    stage.DialogueAfterId = "dlg.ch" + chapter + ".outro";
                }
                stage.HasCinematic = stage.BossKind >= BossKind.ChapterBoss || HasCharacterEvent(id);
            }

            if (stage.IsBoss)
            {
                stage.BossId = "boss.a" + act + ".c" + chapter + "." + stage.BossKind.ToString().ToLowerInvariant();
                if (stage.BossKind == BossKind.FinalBoss)
                {
                    stage.BossId = "boss.valdorax";
                }
            }
            return stage;
        }

        /// <summary>Stage where each new board mechanic appears for the first time (with its short tutorial).</summary>
        public const int TimeBombIntroStage = 101;

        public const int BlightIntroStage = 201;

        public const int EggIntroStage = 301;

        /// <summary>
        /// A new mechanic every 100 stages: countdown bombs (101), spreading blight (201), dragon eggs (301). Each one
        /// then returns on about one stage in five, and they mix as the campaign goes on. Never on boss stages.
        /// </summary>
        private static void AddMechanics(StageData stage, int campaignId, int index, int d)
        {
            if (stage.IsBoss)
            {
                return;
            }
            if (campaignId == TimeBombIntroStage || (campaignId > TimeBombIntroStage && index % 5 == 1))
            {
                stage.TimeBombCount = campaignId == TimeBombIntroStage ? 1 : 1 + (d >= 650 ? 1 : 0);
                stage.TimeBombMoves = campaignId == TimeBombIntroStage ? 12 : 10 - d * 3 / 1000;
            }
            if (campaignId == BlightIntroStage || (campaignId > BlightIntroStage && index % 5 == 3))
            {
                stage.BlightCount = campaignId == BlightIntroStage ? 2 : 2 + d * 2 / 1000;
            }
            if (campaignId == EggIntroStage || (campaignId > EggIntroStage && index % 5 == 2))
            {
                stage.EggCount = campaignId == EggIntroStage ? 3 : 2 + d * 2 / 1000;
            }
            // At most a quarter of the board is blocks.
            int spare = 16 - stage.BlightCount - stage.EggCount;
            stage.StoneCount = Math.Max(0, Math.Min(stage.StoneCount, spare));
            foreach (StageObjective objective in stage.Objectives)
            {
                if (objective.Type == ObjectiveType.BreakStones)
                {
                    objective.Target = Math.Max(1, Math.Min(objective.Target, stage.StoneCount * GoalObstaclePermille / 1000));
                }
            }
        }

        private BossKind BossKindFor(int id, int act, int chapter, int index, bool endless)
        {
            StoryBalance s = _balance.Story;
            if (index == s.MiniBossIndex)
            {
                return BossKind.MiniBoss;
            }
            if (index != s.FinalBossIndex)
            {
                return BossKind.None;
            }
            if (!endless && id == s.TotalStages)
            {
                return BossKind.FinalBoss;
            }
            return chapter % s.ChaptersPerAct == 0 ? BossKind.ActBoss : BossKind.ChapterBoss;
        }

        private static int BossPhasesFor(BossKind kind)
        {
            switch (kind)
            {
                case BossKind.MiniBoss: return 2;
                case BossKind.ChapterBoss: return 3;
                case BossKind.ActBoss: return 4;
                default: return 5;
            }
        }

        /// <summary>
        /// Caps every goal to a share of what an expert bot actually reaches on this exact stage (same seed, all moves,
        /// see StageTuning.g.cs generated by tools/StageAudit). Unlucky boards can never produce an impossible stage.
        /// </summary>
        /// <summary>
        /// Sawtooth inside each chapter (from stage 14): stages 7 and 14 are Hard, 17 is Super hard; bosses keep their own
        /// tuning. The stage right after a hard one is a breather (see <see cref="TierShare"/>).
        /// </summary>
        public static StageTier TierFor(int campaignId, int indexInChapter, bool boss)
        {
            if (boss || campaignId < 14)
            {
                return StageTier.Normal;
            }
            switch (indexInChapter)
            {
                case 7:
                case 14:
                    return StageTier.Hard;
                case 17:
                    return StageTier.SuperHard;
                default:
                    return StageTier.Normal;
            }
        }

        /// <summary>About one stage in five is timed (unlimited moves against the clock), from stage 12, never a boss.</summary>
        public static bool IsTimed(int campaignId, int indexInChapter, bool boss) =>
            !boss && campaignId >= 12 && (indexInChapter == 4 || indexInChapter == 9 || indexInChapter == 12 || indexInChapter == 16);

        /// <summary>Extra share of the expert's reach demanded by the tier; negative on the breather after a hard stage.</summary>
        private static int TierShare(StageData stage)
        {
            switch (stage.Tier)
            {
                case StageTier.Hard: return TunedHardExtraPermille;
                case StageTier.SuperHard: return TunedSuperHardExtraPermille;
            }
            int index = stage.IndexInChapter;
            return stage.Id >= 14 && (index == 8 || index == 15 || index == 18) ? -TunedBreatherPermille : 0;
        }

        /// <summary>
        /// Ceiling a target may never pass: what a mid-skill run reaches on a median board, with a little room on top
        /// so the stage still has to be earned. Without it a board the expert exploits becomes a wall for everyone.
        /// </summary>
        /// <summary>What a mid-skill run reaches on this stage, as measured by the bake (0 when unknown).</summary>
        public static int MidReach(int stageId) =>
            stageId > 0 && stageId < StageTuning.Average.Length ? StageTuning.Average[stageId] : 0;

        private static int Reachable(int id, int onboarding, StageTier tier)
        {
            if (id >= StageTuning.Average.Length || StageTuning.Average[id] <= 0)
            {
                return int.MaxValue;
            }
            bool gated = tier == StageTier.SuperHard && id >= BoostGateFromStage;
            long ceiling = (long)StageTuning.Average[id] * (gated ? BoostGateCeilingPermille : MidReachCeilingPermille) / 1000;
            return (int)Math.Max(200, ceiling * onboarding / 1000);
        }

        /// <summary>True when this stage is meant to need a loadout: the map marks it so nobody walks in unprepared.</summary>
        public static bool NeedsBoosts(StageData stage) =>
            stage != null && !stage.IsBoss && stage.Tier == StageTier.SuperHard && stage.Id >= BoostGateFromStage;

        private static void ApplyTuning(StageData stage, int id)
        {
            if (id >= StageTuning.Score.Length)
            {
                return;
            }
            int score = StageTuning.Score[id];
            int d = stage.DifficultyPermille;
            // The share of the expert's reach grows with difficulty: generous early, demanding late (never above ~90%),
            // then the sawtooth: hard stages ask more, the stage after them less.
            int tierShare = TierShare(stage);
            bool raise = stage.Tier != StageTier.Normal;
            // The opening has to be won. Driving every target from the measured reach made the whole campaign tense,
            // including the first twenty stages, where a new player must feel good rather than tested: the share
            // eases in from 62% of its value and reaches full strength at stage 26.
            int onboarding = id < OnboardingEase
                ? OnboardingFloorPermille + (1000 - OnboardingFloorPermille) * (id - 1) / (OnboardingEase - 1)
                : 1000;
            int scoreShare = Math.Min(TunedMaxSharePermille, TunedScoreEasyPermille + (TunedScoreHardPermille - TunedScoreEasyPermille) * d / 1000 + tierShare) * onboarding / 1000;
            int bossShare = scoreShare - TunedBossReliefPermille;
            int obstacleShare = Math.Min(1000, TunedObstacleEasyPermille + (TunedObstacleHardPermille - TunedObstacleEasyPermille) * d / 1000 + tierShare) * onboarding / 1000;
            if (NeedsBoosts(stage))
            {
                // A gated stage asks for slightly more than a perfect bare-handed run collects, which is exactly
                // what makes it a gate: the missing slice has to come from the loadout. Every one of these stages is
                // won on gems rather than on score, so this -- not the score -- is the number that decides the match.
                obstacleShare = BoostGateObstaclePermille;
            }
            foreach (StageObjective objective in stage.Objectives)
            {
                switch (objective.Type)
                {
                    case ObjectiveType.ReachScore when score > 0:
                        // Always the measured share, never "the smaller of the two". Capping only downwards left 541
                        // stages asking for barely half of what a player actually scores, which is why more than half
                        // the campaign could not be lost and had no tension in it at all.
                        int tunedScore = Math.Max(200, RoundTo((int)((long)score * scoreShare / 1000), 50));
                        tunedScore = Math.Min(tunedScore, Reachable(id, onboarding, stage.Tier));
                        if (NeedsBoosts(stage) && MidReach(id) > 0)
                        {
                            // The cap above only permits a higher target; this is what actually asks for it. Held
                            // under a perfect run so the stage stays theoretically winnable bare-handed, and well
                            // over a mid run so in practice it is the loadout that carries it.
                            int gate = RoundTo((int)((long)MidReach(id) * BoostGateCeilingPermille / 1000), 50);
                            int perfect = RoundTo((int)((long)score * BoostGatePerfectCapPermille / 1000), 50);
                            tunedScore = Math.Max(tunedScore, Math.Min(gate, perfect));
                        }
                        objective.Target = tunedScore;
                        stage.TargetScore = tunedScore;
                        break;
                    case ObjectiveType.DefeatBoss when score > 0:
                        int cappedHp = Math.Max(200, RoundTo((int)((long)score * bossShare / 1000), 50));
                        if (objective.Target > cappedHp)
                        {
                            objective.Target = cappedHp;
                            stage.BossHp = cappedHp;
                            stage.TargetScore = cappedHp;
                        }
                        break;
                    // Floors, capped by what the bake says is actually breakable: a goal is either a real ask or
                    // everything the board has. "Break 2 stones" cleared itself in the first cascade and taught the
                    // player that objectives are decoration.
                    case ObjectiveType.ClearIce when StageTuning.Ice[id] > 0:
                        objective.Target = Floor(StageTuning.Ice[id], obstacleShare, 6);
                        break;
                    case ObjectiveType.BreakStones when StageTuning.Stones[id] > 0:
                        objective.Target = Floor(StageTuning.Stones[id], obstacleShare, 5);
                        break;
                    case ObjectiveType.CollectColor when StageTuning.Collect[id] > 0:
                        int collectShare = Math.Max(400, obstacleShare - TunedCollectReliefPermille);
                        objective.Target = Floor(StageTuning.Collect[id], collectShare, 8);
                        break;
                }
            }
        }

        /// <summary>
        /// A share of what the bake measured, never under <paramref name="floor"/>.
        ///
        /// Normally the measurement is also the ceiling: a goal may not ask for more gems than a perfect run gets.
        /// A share above 1000 lifts that ceiling on purpose, and only the boost-gated stages do it.
        /// </summary>
        private static int Floor(int measured, int share, int floor)
        {
            int scaled = (int)((long)measured * share / 1000);
            int cap = share > 1000 ? scaled : measured;
            return Math.Max(1, Math.Min(cap, Math.Max(floor, scaled)));
        }

        private static StageObjective CollectObjective(StageData stage, DeterministicRandom rng, int scalePermille = 1000)
        {
            int amount = Math.Max(8, stage.MoveLimit * 450 / 1000 * scalePermille / 1000);
            return new StageObjective
            {
                Type = ObjectiveType.CollectColor,
                Target = amount,
                Color = (PieceColor)rng.NextInt(stage.ColorCount)
            };
        }

        private static bool HasCharacterEvent(int stageId)
        {
            foreach (CharacterDefinition c in StoryDatabase.Characters)
            {
                if (c.JoinStage == stageId || c.LeaveStage == stageId || c.ReturnStage == stageId)
                {
                    return true;
                }
            }
            return false;
        }

        private static int Lerp(int easy, int hard, int permille) => easy + (int)((long)(hard - easy) * permille / 1000);

        private static int RoundTo(int value, int step) => Math.Max(step, (value + step / 2) / step * step);
    }
}
